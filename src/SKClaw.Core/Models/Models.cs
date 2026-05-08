namespace SKClaw.Core.Models;

public record AgentResponse
{
    public string SessionId { get; init; } = "";
    public string Content { get; init; } = "";
    public string Role { get; init; } = "assistant";
    public DateTimeOffset Timestamp { get; init; }
    public string? TokensUsed { get; init; }
    public List<ToolCall> ToolCalls { get; init; } = [];
    public bool IsError => Role == "error";
}

public record ToolCall
{
    public string Name { get; init; } = "";
    public string Arguments { get; init; } = "";
    public string? Result { get; init; }
    public TimeSpan Duration { get; init; }
}

public record ChatRequest
{
    public string Message { get; init; } = "";
    public string? SessionId { get; init; }
    public string? Provider { get; init; }
    public bool Stream { get; init; } = false;
    public Dictionary<string, object> Metadata { get; init; } = [];
}

public record ChatSession
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public string Name { get; set; } = "New Chat";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastActivityAt { get; set; } = DateTimeOffset.UtcNow;
    public List<AgentResponse> History { get; init; } = [];
    public string Provider { get; set; } = "";
}

public record SystemStatus
{
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public string Environment { get; init; } = "";
    public bool IsHealthy { get; init; }
    public string LLMProvider { get; init; } = "";
    public string LLMModel { get; init; } = "";
    public string MemoryProvider { get; init; } = "";
    public bool MCPEnabled { get; init; }
    public List<string> EnabledChannels { get; init; } = [];
    public List<string> LoadedPlugins { get; init; } = [];
    public DateTimeOffset StartedAt { get; init; }
    public TimeSpan Uptime => DateTimeOffset.UtcNow - StartedAt;
}

public record McpTool
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public object InputSchema { get; init; } = new { };
}
