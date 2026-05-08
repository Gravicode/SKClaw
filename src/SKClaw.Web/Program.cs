using System.Configuration;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.SemanticKernel;
using SKClaw.Core.Agents;
using SKClaw.Core.Channels;
using SKClaw.Core.Configuration;
using SKClaw.Core.Connectors;
using SKClaw.Core.MCP;
using SKClaw.Core.Memory;
using SKClaw.Core.Skills;
using SKClaw.Web.Hubs;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ── Load Configuration from app.config ──────────────────────
var appConfig = new AppConfiguration(System.Configuration.ConfigurationManager.AppSettings);

// ── Logging ──────────────────────────────────────────────────
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// ── Services ─────────────────────────────────────────────────
builder.Services.AddSingleton(appConfig);

// Kernel Factory
builder.Services.AddSingleton<KernelFactory>();
builder.Services.AddSingleton<Kernel>(sp =>
{
    var factory = sp.GetRequiredService<KernelFactory>();
    return factory.CreateKernel();
});

// Memory
builder.Services.AddSingleton<SkClawMemory>();

// Agent (scoped so each request/session can have its own)
builder.Services.AddScoped<SkClawAgent>(sp =>
{
    var kernel = sp.GetRequiredService<Kernel>();
    var config = sp.GetRequiredService<AppConfiguration>();
    var logger = sp.GetRequiredService<ILogger<SkClawAgent>>();
    var memory = sp.GetRequiredService<SkClawMemory>();

    // Register plugins
    PluginRegistry.RegisterAll(kernel, config);

    return new SkClawAgent(kernel, config, logger, memory);
});

// MCP Server
builder.Services.AddSingleton<SkClawMcpServer>(sp =>
{
    var kernel = sp.GetRequiredService<Kernel>();
    var config = sp.GetRequiredService<AppConfiguration>();
    var logger = sp.GetRequiredService<ILogger<SkClawMcpServer>>();
    PluginRegistry.RegisterAll(kernel, config);
    return new SkClawMcpServer(config, kernel, logger);
});

// MCP Client Manager
builder.Services.AddSingleton<McpClientManager>();

// Channels
builder.Services.AddSingleton<TelegramChannel>();
builder.Services.AddSingleton<DiscordChannel>();
builder.Services.AddSingleton<SlackChannel>();
builder.Services.AddSingleton<WhatsAppChannel>();
builder.Services.AddSingleton<IEnumerable<IChannel>>(sp => [
    sp.GetRequiredService<TelegramChannel>(),
    sp.GetRequiredService<DiscordChannel>(),
    sp.GetRequiredService<SlackChannel>(),
    sp.GetRequiredService<WhatsAppChannel>(),
]);
builder.Services.AddSingleton<ChannelManager>();

// JWT Authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(appConfig.Web.Admin.JwtSecret))
        };

        // Allow JWT from cookie (for web chat)
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Cookies["skclaw_token"]
                    ?? ctx.Request.Query["access_token"].ToString();
                if (!string.IsNullOrEmpty(token))
                    ctx.Token = token;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// CORS
builder.Services.AddCors(opt =>
{
    var origins = appConfig.Web.AllowedOrigins;
    opt.AddDefaultPolicy(p =>
    {
        if (origins == "*")
            p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        else
            p.WithOrigins(origins.Split(','))
             .AllowAnyMethod().AllowAnyHeader().AllowCredentials();
    });
});

// Rate Limiting
builder.Services.AddRateLimiter(opt =>
{
    opt.AddFixedWindowLimiter("api", o =>
    {
        o.Window = TimeSpan.FromMinutes(1);
        o.PermitLimit = appConfig.Web.Api.RateLimitPerMinute;
        o.QueueLimit = 0;
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

// SignalR for real-time chat
builder.Services.AddSignalR(opt =>
{
    opt.MaximumReceiveMessageSize = 1024 * 1024; // 1MB
    opt.EnableDetailedErrors = appConfig.App.Environment == "Development";
});

// Controllers + API Explorer
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// HttpContextAccessor (for channels)
builder.Services.AddHttpContextAccessor();

// ── Build App ────────────────────────────────────────────────
var app = builder.Build();

// Configure server URLs from app.config
var url = $"{(appConfig.Web.UseHttps ? "https" : "http")}://{appConfig.Web.Host}:{appConfig.Web.Port}";
app.Urls.Add(url);

// ── Middleware Pipeline ───────────────────────────────────────
if (appConfig.App.Environment == "Development")
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "SKClaw API v1"));
}

app.UseCors();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// ── Routes ────────────────────────────────────────────────────

// Web Chat (SPA served as static file)
app.MapGet("/", () => Results.Redirect("/chat"));
app.MapGet("/chat", () => Results.File(
    Path.Combine(builder.Environment.WebRootPath, "index.html"), "text/html"));
app.MapGet("/admin", () => Results.File(
    Path.Combine(builder.Environment.WebRootPath, "admin.html"), "text/html"));

// SignalR Hub
app.MapHub<ChatHub>("/hubs/chat");

// API Controllers
app.MapControllers();

// MCP SSE Endpoint
if (appConfig.MCP.Enabled)
{
    app.MapGet(appConfig.MCP.SsePath, async (HttpContext ctx,
        SkClawMcpServer mcpServer, CancellationToken ct) =>
    {
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers["Cache-Control"] = "no-cache";
        ctx.Response.Headers["X-Accel-Buffering"] = "no";

        var tools = mcpServer.GetExposedTools();
        var initEvent = System.Text.Json.JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "initialize",
            @params = new
            {
                serverInfo = new { name = appConfig.MCP.ServerName, version = appConfig.MCP.Version },
                capabilities = new { tools = new { listChanged = false } }
            }
        });

        await ctx.Response.WriteAsync($"data: {initEvent}\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);

        // Keep connection alive
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(15000, ct);
            await ctx.Response.WriteAsync(": keepalive\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
        }
    });

    // MCP Message endpoint (tool calls)
    app.MapPost(appConfig.MCP.MessagePath, async (HttpContext ctx,
        SkClawMcpServer mcpServer, CancellationToken ct) =>
    {
        var body = await new System.IO.StreamReader(ctx.Request.Body).ReadToEndAsync(ct);
        var request = System.Text.Json.JsonDocument.Parse(body).RootElement;

        var method = request.GetProperty("method").GetString();
        var id = request.GetProperty("id").GetRawText();

        if (method == "tools/list")
        {
            var tools = mcpServer.GetExposedTools();
            return Results.Ok(new
            {
                jsonrpc = "2.0",
                id = System.Text.Json.JsonSerializer.Deserialize<object>(id),
                result = new { tools }
            });
        }

        if (method == "tools/call")
        {
            var p = request.GetProperty("params");
            var toolName = p.GetProperty("name").GetString() ?? "";
            var args = p.TryGetProperty("arguments", out var a)
                ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(a.GetRawText()) ?? []
                : [];

            var result = await mcpServer.ExecuteToolAsync(toolName, args, ct);
            return Results.Ok(new
            {
                jsonrpc = "2.0",
                id = System.Text.Json.JsonSerializer.Deserialize<object>(id),
                result = new
                {
                    content = new[] { new { type = "text", text = result.Content } },
                    isError = result.IsError
                }
            });
        }

        return Results.BadRequest(new { error = $"Unknown method: {method}" });
    });
}

// Slack Events webhook
app.MapPost("/webhooks/slack", async (HttpContext ctx,
    SlackChannel slack, CancellationToken ct) =>
{
    if (!slack.IsEnabled) return Results.NotFound();

    var body = await new System.IO.StreamReader(ctx.Request.Body).ReadToEndAsync(ct);
    var sig = ctx.Request.Headers["X-Slack-Signature"].ToString();
    var ts = ctx.Request.Headers["X-Slack-Request-Timestamp"].ToString();

    if (!slack.VerifySignature(sig, ts, body))
        return Results.Unauthorized();

    var payload = System.Text.Json.JsonDocument.Parse(body).RootElement;
    var result = await slack.HandleEventAsync(payload, ct);
    return Results.Ok(string.IsNullOrEmpty(result) ? new { } : (object)new { challenge = result });
});

// WhatsApp webhook (Twilio)
app.MapPost("/webhooks/whatsapp", async (HttpContext ctx,
    WhatsAppChannel whatsapp, CancellationToken ct) =>
{
    if (!whatsapp.IsEnabled) return Results.NotFound();

    var form = await ctx.Request.ReadFormAsync(ct);
    var from = form["From"].ToString();
    var body = form["Body"].ToString();
    await whatsapp.HandleWebhookAsync(from, body, ct);
    return Results.Ok(new { status = "received" });
});

// Telegram webhook
app.MapPost("/webhooks/telegram", async (HttpContext ctx,
    TelegramChannel telegram, CancellationToken ct) =>
{
    // Telegram webhook handling is internal to channel
    return Results.Ok();
});

// Discord interactions
app.MapPost("/webhooks/discord", async (HttpContext ctx,
    DiscordChannel discord, CancellationToken ct) =>
{
    if (!discord.IsEnabled) return Results.NotFound();

    var body = await new System.IO.StreamReader(ctx.Request.Body).ReadToEndAsync(ct);
    var interaction = System.Text.Json.JsonSerializer.Deserialize<DiscordChannel.DiscordInteraction>(body);
    if (interaction != null)
        await discord.HandleInteractionAsync(interaction, ct);
    return Results.Ok(new { type = 1 }); // ACK
});

// ── Initialize & Start ────────────────────────────────────────
var logger = app.Services.GetRequiredService<ILogger<Program>>();

// Start channels
var channelManager = app.Services.GetRequiredService<ChannelManager>();
var cts = new CancellationTokenSource();

var channels = channelManager.GetActiveChannels();
if (channels.Count > 0)
    logger.LogInformation("Active channels: {Channels}", string.Join(", ", channels));

_ = Task.Run(async () =>
{
    await Task.Delay(1000); // wait for web server to start
    await channelManager.StartAllAsync(cts.Token);
}, cts.Token);

// Initialize MCP client connections
var mcpClient = app.Services.GetRequiredService<McpClientManager>();
await mcpClient.InitializeAsync();

logger.LogInformation("""

 ███████╗██╗  ██╗ ██████╗██╗      █████╗ ██╗    ██╗
 ██╔════╝██║ ██╔╝██╔════╝██║     ██╔══██╗██║    ██║
 ███████╗█████╔╝ ██║     ██║     ███████║██║ █╗ ██║
 ╚════██║██╔═██╗ ██║     ██║     ██╔══██║██║███╗██║
 ███████║██║  ██╗╚██████╗███████╗██║  ██║╚███╔███╔╝
 ╚══════╝╚═╝  ╚═╝ ╚═════╝╚══════╝╚═╝  ╚═╝ ╚══╝╚══╝

 Version: {Version} | Provider: {Provider} | URL: {Url}
""",
    appConfig.App.Version, appConfig.LLM.DefaultProvider, url);

app.Run();
