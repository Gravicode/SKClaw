using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SKClaw.Core.Agents;
using SKClaw.Core.Configuration;
using SKClaw.Core.MCP;
using SKClaw.Core.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace SKClaw.Web.Controllers;

// ─────────────────────────────────────────────────────────────
// CHAT API (Integration API - OpenAI-compatible format)
// ─────────────────────────────────────────────────────────────
[ApiController]
[Route("api/chat")]
[EnableRateLimiting("api")]
public class ChatApiController : ControllerBase
{
    private readonly SkClawAgent _agent;
    private readonly AppConfiguration _config;

    public ChatApiController(SkClawAgent agent, AppConfiguration config)
    {
        _agent = agent;
        _config = config;
    }

    /// <summary>
    /// OpenAI-compatible chat completions endpoint.
    /// POST /api/chat/completions
    /// </summary>
    [HttpPost("completions")]
    public async Task<IActionResult> ChatCompletionsAsync(
        [FromBody] OpenAICompatibleRequest request,
        CancellationToken ct)
    {
        if (!ValidateApiKey()) return Unauthorized(new { error = "Invalid API key" });

        var userMessage = request.Messages.LastOrDefault(m => m.Role == "user")?.Content ?? "";
        if (string.IsNullOrEmpty(userMessage))
            return BadRequest(new { error = "No user message found" });

        if (request.Stream)
        {
            Response.ContentType = "text/event-stream";
            Response.Headers["Cache-Control"] = "no-cache";

            await foreach (var chunk in _agent.ChatStreamAsync(userMessage, ct))
            {
                var data = System.Text.Json.JsonSerializer.Serialize(new
                {
                    id = $"chatcmpl-{Guid.NewGuid():N}",
                    @object = "chat.completion.chunk",
                    created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    model = _config.LLM.DefaultModel,
                    choices = new[]
                    {
                        new { index = 0, delta = new { content = chunk }, finish_reason = (string?)null }
                    }
                });
                await Response.WriteAsync($"data: {data}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }

            await Response.WriteAsync("data: [DONE]\n\n", ct);
            return new EmptyResult();
        }
        else
        {
            var response = await _agent.ChatAsync(userMessage, request.SessionId, ct);
            return Ok(new
            {
                id = $"chatcmpl-{Guid.NewGuid():N}",
                @object = "chat.completion",
                created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                model = _config.LLM.DefaultModel,
                choices = new[]
                {
                    new
                    {
                        index = 0,
                        message = new { role = "assistant", content = response.Content },
                        finish_reason = "stop"
                    }
                },
                usage = new { prompt_tokens = 0, completion_tokens = 0, total_tokens = 0 }
            });
        }
    }

    /// <summary>
    /// Simple chat endpoint.
    /// POST /api/chat
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ChatAsync([FromBody] ChatRequest request, CancellationToken ct)
    {
        if (!ValidateApiKey()) return Unauthorized(new { error = "Invalid API key" });

        var response = await _agent.ChatAsync(request.Message, request.SessionId, ct);
        return Ok(response);
    }

    /// <summary>
    /// Stream chat endpoint.
    /// POST /api/chat/stream
    /// </summary>
    [HttpPost("stream")]
    public async Task StreamChatAsync([FromBody] ChatRequest request, CancellationToken ct)
    {
        if (!ValidateApiKey())
        {
            Response.StatusCode = 401;
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";

        await foreach (var chunk in _agent.ChatStreamAsync(request.Message, ct))
        {
            await Response.WriteAsync($"data: {System.Text.Json.JsonSerializer.Serialize(new { chunk })}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }

        await Response.WriteAsync("data: [DONE]\n\n", ct);
    }

    private bool ValidateApiKey()
    {
        if (!_config.Web.Api.RequireAuth) return true;

        // Check Authorization header
        var authHeader = Request.Headers.Authorization.ToString();
        if (authHeader.StartsWith("Bearer "))
        {
            var key = authHeader["Bearer ".Length..];
            return _config.Web.Api.ApiKeys.Contains(key);
        }

        // Check X-API-Key header
        var apiKey = Request.Headers["X-API-Key"].ToString();
        if (!string.IsNullOrEmpty(apiKey))
            return _config.Web.Api.ApiKeys.Contains(apiKey);

        return false;
    }
}

// ─────────────────────────────────────────────────────────────
// ADMIN API
// ─────────────────────────────────────────────────────────────
[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly AppConfiguration _config;
    private readonly SkClawMcpServer _mcpServer;
    private readonly ChannelManager _channelManager;

    public AdminController(AppConfiguration config, SkClawMcpServer mcpServer,
        ChannelManager channelManager)
    {
        _config = config;
        _mcpServer = mcpServer;
        _channelManager = channelManager;
    }

    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        if (request.Username != _config.Web.Admin.Username ||
            request.Password != _config.Web.Admin.Password)
            return Unauthorized(new { error = "Invalid credentials" });

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_config.Web.Admin.JwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            claims: [new Claim(ClaimTypes.Name, request.Username),
                     new Claim(ClaimTypes.Role, "admin")],
            expires: DateTime.UtcNow.AddHours(_config.Web.Admin.JwtExpiryHours),
            signingCredentials: creds);

        var tokenStr = new JwtSecurityTokenHandler().WriteToken(token);

        Response.Cookies.Append("skclaw_token", tokenStr, new CookieOptions
        {
            HttpOnly = true,
            Secure = _config.Web.UseHttps,
            Expires = DateTimeOffset.UtcNow.AddHours(_config.Web.Admin.JwtExpiryHours)
        });

        return Ok(new { token = tokenStr, expiresIn = _config.Web.Admin.JwtExpiryHours * 3600 });
    }

    [HttpPost("logout")]
    [Authorize]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("skclaw_token");
        return Ok(new { message = "Logged out" });
    }

    [HttpGet("status")]
    [Authorize]
    public IActionResult GetStatus()
    {
        var status = new SystemStatus
        {
            Name = _config.App.Name,
            Version = _config.App.Version,
            Environment = _config.App.Environment,
            IsHealthy = true,
            LLMProvider = _config.LLM.DefaultProvider,
            LLMModel = _config.LLM.DefaultModel,
            MemoryProvider = _config.Memory.Provider,
            MCPEnabled = _config.MCP.Enabled,
            EnabledChannels = _channelManager.GetActiveChannels().ToList(),
            LoadedPlugins = _config.Plugins.EnabledSkills.ToList(),
            StartedAt = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(Environment.TickCount64 / 1000.0)
        };
        return Ok(status);
    }

    [HttpGet("tools")]
    [Authorize]
    public IActionResult GetTools() => Ok(_mcpServer.GetExposedTools());

    [HttpGet("config")]
    [Authorize]
    public IActionResult GetConfig()
    {
        // Return sanitized config (no secrets)
        return Ok(new
        {
            app = new { _config.App.Name, _config.App.Version, _config.App.Environment },
            llm = new
            {
                _config.LLM.DefaultProvider,
                _config.LLM.DefaultModel,
                _config.LLM.MaxTokens,
                _config.LLM.Temperature
            },
            memory = new { _config.Memory.Provider, _config.Memory.CollectionName },
            mcp = new { _config.MCP.Enabled, _config.MCP.Transport },
            channels = new
            {
                telegram = _config.Channels.Telegram.Enabled,
                discord = _config.Channels.Discord.Enabled,
                slack = _config.Channels.Slack.Enabled,
                whatsapp = _config.Channels.WhatsApp.Enabled,
            },
            plugins = new { _config.Plugins.EnabledSkills }
        });
    }
}

// ─────────────────────────────────────────────────────────────
// HEALTH CHECK
// ─────────────────────────────────────────────────────────────
[ApiController]
[Route("api")]
public class HealthController : ControllerBase
{
    private readonly AppConfiguration _config;

    public HealthController(AppConfiguration config) => _config = config;

    [HttpGet("health")]
    public IActionResult Health() => Ok(new
    {
        status = "healthy",
        name = _config.App.Name,
        version = _config.App.Version,
        timestamp = DateTimeOffset.UtcNow
    });

    [HttpGet("models")]
    public IActionResult GetModels()
    {
        // Return available models (OpenAI-compatible format)
        return Ok(new
        {
            @object = "list",
            data = new[]
            {
                new { id = _config.LLM.DefaultModel, @object = "model", owned_by = _config.LLM.DefaultProvider }
            }
        });
    }
}

// ─────────────────────────────────────────────────────────────
// MCP API (REST endpoints for MCP operations)
// ─────────────────────────────────────────────────────────────
[ApiController]
[Route("api/mcp")]
[EnableRateLimiting("api")]
public class McpApiController : ControllerBase
{
    private readonly SkClawMcpServer _mcpServer;
    private readonly AppConfiguration _config;

    public McpApiController(SkClawMcpServer mcpServer, AppConfiguration config)
    {
        _mcpServer = mcpServer;
        _config = config;
    }

    [HttpGet("tools")]
    public IActionResult ListTools() => Ok(new { tools = _mcpServer.GetExposedTools() });

    [HttpPost("tools/{toolName}")]
    public async Task<IActionResult> CallTool(
        string toolName,
        [FromBody] Dictionary<string, object?> arguments,
        CancellationToken ct)
    {
        var result = await _mcpServer.ExecuteToolAsync(toolName, arguments, ct);
        return result.IsError ? BadRequest(new { error = result.Content }) : Ok(new { result = result.Content });
    }
}

// ─────────────────────────────────────────────────────────────
// CHANNEL MANAGER (accessible from DI)
// ─────────────────────────────────────────────────────────────
public class ChannelManager
{
    private readonly SKClaw.Core.Channels.ChannelManager _inner;

    public ChannelManager(SKClaw.Core.Channels.ChannelManager inner) => _inner = inner;

    public IReadOnlyList<string> GetActiveChannels() => _inner.GetActiveChannels();
}

// ─────────────────────────────────────────────────────────────
// REQUEST/RESPONSE MODELS
// ─────────────────────────────────────────────────────────────
public record LoginRequest(string Username, string Password);

public record OpenAICompatibleRequest
{
    public string Model { get; init; } = "";
    public List<OpenAIChatMessage> Messages { get; init; } = [];
    public bool Stream { get; init; }
    public int? MaxTokens { get; init; }
    public double? Temperature { get; init; }
    public string? SessionId { get; init; }
}

public record OpenAIChatMessage(string Role, string Content);
