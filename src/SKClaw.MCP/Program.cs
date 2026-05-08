using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Server;
using SKClaw.Core.Agents;
using SKClaw.Core.Configuration;
using SKClaw.Core.Connectors;
using SKClaw.Core.Memory;
using SKClaw.Core.MCP;
using SKClaw.Core.Skills;

// Use fully qualified name to avoid ambiguity between System.Configuration and Microsoft.Extensions.Configuration
var appConfig = new AppConfiguration(System.Configuration.ConfigurationManager.AppSettings);

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Information);

builder.Services.AddSingleton(appConfig);
builder.Services.AddSingleton<KernelFactory>();
builder.Services.AddSingleton(sp =>
{
    var factory = sp.GetRequiredService<KernelFactory>();
    var kernel = factory.CreateKernel();
    PluginRegistry.RegisterAll(kernel, appConfig);
    return kernel;
});
builder.Services.AddSingleton<SkClawMemory>();
builder.Services.AddSingleton<SkClawMcpServer>();
builder.Services.AddTransient<SkClawAgent>(sp =>
{
    var kernel = sp.GetRequiredService<Kernel>();
    var config = sp.GetRequiredService<AppConfiguration>();
    var logger = sp.GetRequiredService<ILogger<SkClawAgent>>();
    var memory = sp.GetRequiredService<SkClawMemory>();
    return new SkClawAgent(kernel, config, logger, memory);
});

// Register MCP Server using ModelContextProtocol library
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<SkClawMcpTools>();

var app = builder.Build();
app.MapMcp();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("SKClaw MCP Server starting on port {Port}", appConfig.Web.Port);
logger.LogInformation("MCP SSE: {SsePath}", appConfig.MCP.SsePath);

app.Run($"http://0.0.0.0:{appConfig.Web.Port + 1}"); // MCP runs on port+1

// ── MCP Tools (using ModelContextProtocol.Server attributes) ──────────────────
[McpServerToolType]
public class SkClawMcpTools
{
    private readonly SkClawMcpServer _server;
    private readonly SkClawAgent _agent;
    private readonly Kernel _kernel;

    public SkClawMcpTools(SkClawMcpServer server, SkClawAgent agent, Kernel kernel)
    {
        _server = server;
        _agent = agent;
        _kernel = kernel;
    }

    [McpServerTool, Description("Chat with the SKClaw AI agent. Supports multi-turn conversations with memory.")]
    public async Task<string> Chat(
        [Description("The message to send to the agent")] string message,
        [Description("Optional session ID for conversation continuity")] string sessionId = "mcp")
    {
        var response = await _agent.ChatAsync(message, sessionId);
        return response.Content;
    }

    [McpServerTool, Description("Execute a prompt template with Semantic Kernel")]
    public async Task<string> RunPrompt(
        [Description("Prompt template (use {{$variable}} for variables)")] string prompt,
        [Description("Variables as JSON object, e.g. {\"name\": \"Alice\"}")] string variables = "{}")
    {
        var vars = System.Text.Json.JsonSerializer
            .Deserialize<Dictionary<string, string>>(variables) ?? [];
        var args = new KernelArguments();
        foreach (var kv in vars) args[kv.Key] = kv.Value;
        var result = await _kernel.InvokePromptAsync(prompt, args);
        return result.GetValue<string>() ?? "";
    }

    [McpServerTool, Description("Get the current date and time")]
    public string GetTime([Description("Timezone (e.g., UTC, Asia/Jakarta)")] string timezone = "UTC")
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
            return TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).ToString("yyyy-MM-dd HH:mm:ss zzz");
        }
        catch { return DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"); }
    }

    [McpServerTool, Description("Calculate a mathematical expression")]
    public string Calculate([Description("Math expression, e.g., '2 + 3 * 4'")] string expression)
    {
        try
        {
            var table = new System.Data.DataTable();
            var result = table.Compute(expression, null);
            return Convert.ToDouble(result).ToString("G");
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    [McpServerTool, Description("Make an HTTP GET request")]
    public async Task<string> HttpGet([Description("URL to fetch")] string url)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var content = await client.GetStringAsync(url);
        return content[..Math.Min(4000, content.Length)];
    }

    [McpServerTool, Description("Summarize a long text")]
    public async Task<string> Summarize(
        [Description("Text to summarize")] string text,
        [Description("Maximum words in summary")] int maxWords = 150)
    {
        var prompt = $"Summarize in at most {maxWords} words as bullet points:\n\n{text}";
        var result = await _kernel.InvokePromptAsync(prompt);
        return result.GetValue<string>() ?? "";
    }

    [McpServerTool, Description("Translate text to another language")]
    public async Task<string> Translate(
        [Description("Text to translate")] string text,
        [Description("Target language")] string targetLanguage)
    {
        var prompt = $"Translate to {targetLanguage}. Return only the translation:\n\n{text}";
        var result = await _kernel.InvokePromptAsync(prompt);
        return result.GetValue<string>() ?? "";
    }

    [McpServerTool, Description("List all available tools in this SKClaw instance")]
    public string ListTools()
    {
        var tools = _server.GetExposedTools();
        return string.Join("\n", tools.Select(t => $"- {t.Name}: {t.Description}"));
    }

    [McpServerTool, Description("Read a file from the workspace")]
    public async Task<string> ReadFile([Description("Filename in workspace")] string filename)
    {
        var workspace = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".skclaw", "workspace");
        var path = Path.GetFullPath(Path.Combine(workspace, filename));
        if (!path.StartsWith(workspace)) return "Access denied: outside workspace";
        if (!File.Exists(path)) return $"File not found: {filename}";
        var content = await File.ReadAllTextAsync(path);
        return content[..Math.Min(8000, content.Length)];
    }

    [McpServerTool, Description("Write content to a file in the workspace")]
    public async Task<string> WriteFile(
        [Description("Filename")] string filename,
        [Description("Content to write")] string content)
    {
        var workspace = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".skclaw", "workspace");
        Directory.CreateDirectory(workspace);
        var path = Path.GetFullPath(Path.Combine(workspace, filename));
        if (!path.StartsWith(workspace)) return "Access denied: outside workspace";
        await File.WriteAllTextAsync(path, content);
        return $"Written {content.Length} chars to {filename}";
    }
}
