using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Server;
using SKClaw.Core.Agents;
using SKClaw.Core.Configuration;
using SKClaw.Core.Models;

namespace SKClaw.Core.MCP;

/// <summary>
/// SKClaw MCP Server - exposes agent capabilities via Model Context Protocol.
/// Other AI agents (Claude, Cursor, etc.) can connect and use SKClaw's tools.
/// Supports: SSE transport (HTTP) and stdio transport.
/// </summary>
public class SkClawMcpServer
{
    private readonly AppConfiguration _config;
    private readonly ILogger<SkClawMcpServer> _logger;
    private readonly Kernel _kernel;

    public SkClawMcpServer(AppConfiguration config, Kernel kernel, ILogger<SkClawMcpServer> logger)
    {
        _config = config;
        _kernel = kernel;
        _logger = logger;
    }

    /// <summary>
    /// Returns the list of tools exposed via MCP.
    /// These are generated from the loaded Semantic Kernel plugins.
    /// </summary>
    public List<McpTool> GetExposedTools()
    {
        var tools = new List<McpTool>();

        // Expose all kernel functions as MCP tools
        foreach (var plugin in _kernel.Plugins)
        {
            foreach (var fn in plugin)
            {
                tools.Add(new McpTool
                {
                    Name = $"{plugin.Name}_{fn.Name}",
                    Description = fn.Description ?? $"{plugin.Name}.{fn.Name}",
                    InputSchema = BuildJsonSchema(fn)
                });
            }
        }

        // Always expose the core chat tool
        tools.Add(new McpTool
        {
            Name = "skclaw_chat",
            Description = "Chat with the SKClaw AI agent. Supports multi-turn conversations.",
            InputSchema = new
            {
                type = "object",
                properties = new
                {
                    message = new { type = "string", description = "The message to send" },
                    sessionId = new { type = "string", description = "Optional session ID for multi-turn" }
                },
                required = new[] { "message" }
            }
        });

        tools.Add(new McpTool
        {
            Name = "skclaw_prompt",
            Description = "Execute a semantic kernel prompt with variables.",
            InputSchema = new
            {
                type = "object",
                properties = new
                {
                    prompt = new { type = "string", description = "The prompt template" },
                    variables = new { type = "object", description = "Variables for the prompt" }
                },
                required = new[] { "prompt" }
            }
        });

        return tools;
    }

    /// <summary>
    /// Execute an MCP tool call.
    /// </summary>
    public async Task<McpToolResult> ExecuteToolAsync(string toolName, 
        Dictionary<string, object?> arguments, CancellationToken ct = default)
    {
        _logger.LogInformation("MCP tool call: {Tool}", toolName);

        try
        {
            if (toolName == "skclaw_chat")
            {
                var message = arguments.GetValueOrDefault("message")?.ToString() ?? "";
                var sessionId = arguments.GetValueOrDefault("sessionId")?.ToString();

                var agent = _kernel.Services.GetService(typeof(SkClawAgent)) as SkClawAgent;
                if (agent == null)
                    return McpToolResult.Error("Agent not available");

                var response = await agent.ChatAsync(message, sessionId, ct);
                return McpToolResult.Success(response.Content);
            }

            if (toolName == "skclaw_prompt")
            {
                var prompt = arguments.GetValueOrDefault("prompt")?.ToString() ?? "";
                var vars = arguments.GetValueOrDefault("variables") as Dictionary<string, object?> ?? [];

                var kernelArgs = new KernelArguments();
                foreach (var kv in vars)
                    kernelArgs[kv.Key] = kv.Value?.ToString() ?? "";

                var result = await _kernel.InvokePromptAsync(prompt, kernelArgs, cancellationToken: ct);
                return McpToolResult.Success(result.GetValue<string>() ?? "");
            }

            // Route to kernel plugin function
            // Format: PluginName_FunctionName
            var parts = toolName.Split('_', 2);
            if (parts.Length == 2)
            {
                var pluginName = parts[0];
                var funcName = parts[1];

                if (_kernel.Plugins.TryGetPlugin(pluginName, out var plugin) &&
                    plugin.TryGetFunction(funcName, out var func))
                {
                    var kernelArgs = new KernelArguments();
                    foreach (var param in func.Metadata.Parameters)
                    {
                        if (arguments.TryGetValue(param.Name, out var val))
                            kernelArgs[param.Name] = val?.ToString() ?? "";
                    }

                    var result = await func.InvokeAsync(_kernel, kernelArgs, ct);
                    return McpToolResult.Success(result.GetValue<string>() ?? result.ToString() ?? "");
                }
            }

            return McpToolResult.Error($"Unknown tool: {toolName}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP tool execution failed: {Tool}", toolName);
            return McpToolResult.Error($"Tool error: {ex.Message}");
        }
    }

    private static object BuildJsonSchema(KernelFunctionMetadata fn)
    {
        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var param in fn.Parameters)
        {
            properties[param.Name] = new
            {
                type = MapType(param.ParameterType),
                description = param.Description ?? param.Name
            };

            if (param.IsRequired)
                required.Add(param.Name);
        }

        return new
        {
            type = "object",
            properties,
            required = required.ToArray()
        };
    }

    private static object BuildJsonSchema(KernelFunction fn) => BuildJsonSchema(fn.Metadata);

    private static string MapType(Type? type)
    {
        if (type == null) return "string";
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t.Name switch
        {
            "Int32" or "Int64" or "Double" or "Float" or "Decimal" => "number",
            "Boolean" => "boolean",
            "String" => "string",
            _ => "string"
        };
    }
}

public record McpToolResult
{
    public bool IsError { get; init; }
    public string Content { get; init; } = "";

    public static McpToolResult Success(string content) => new() { Content = content };
    public static McpToolResult Error(string message) => new() { IsError = true, Content = message };
}

/// <summary>
/// MCP Client - connects to external MCP servers and imports their tools.
/// </summary>
public class McpClientManager
{
    private readonly AppConfiguration _config;
    private readonly ILogger<McpClientManager> _logger;
    private readonly List<ExternalMcpServer> _servers = [];

    public McpClientManager(AppConfiguration config, ILogger<McpClientManager> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_config.MCP.ExternalServers)) return;

        try
        {
            var servers = System.Text.Json.JsonSerializer.Deserialize<List<ExternalMcpServer>>(
                _config.MCP.ExternalServers) ?? [];

            foreach (var server in servers)
            {
                _servers.Add(server);
                _logger.LogInformation("Registered external MCP server: {Name} @ {Url}",
                    server.Name, server.Url);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse MCP:ExternalServers config");
        }
    }

    public IReadOnlyList<ExternalMcpServer> GetServers() => _servers.AsReadOnly();

    public record ExternalMcpServer
    {
        public string Name { get; init; } = "";
        public string Url { get; init; } = "";
        public string Transport { get; init; } = "sse";
        public Dictionary<string, string> Headers { get; init; } = [];
    }
}
