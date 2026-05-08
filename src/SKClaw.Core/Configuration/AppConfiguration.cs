using System.Configuration;

namespace SKClaw.Core.Configuration;

/// <summary>
/// Centralized configuration reader from app.config appSettings.
/// All settings are read from a single app.config file.
/// </summary>
public class AppConfiguration
{
    private readonly System.Collections.Specialized.NameValueCollection _settings;

    public AppConfiguration(System.Collections.Specialized.NameValueCollection? settings = null)
    {
        _settings = settings ?? ConfigurationManager.AppSettings;
        App = new AppSettings(_settings);
        LLM = new LLMSettings(_settings);
        Memory = new MemorySettings(_settings);
        MCP = new McpSettings(_settings);
        Web = new WebSettings(_settings);
        Channels = new ChannelsSettings(_settings);
        Plugins = new PluginsSettings(_settings);
        Agent = new AgentSettings(_settings);
        Storage = new StorageSettings(_settings);
        Telemetry = new TelemetrySettings(_settings);
    }

    public AppSettings App { get; }
    public LLMSettings LLM { get; }
    public MemorySettings Memory { get; }
    public McpSettings MCP { get; }
    public WebSettings Web { get; }
    public ChannelsSettings Channels { get; }
    public PluginsSettings Plugins { get; }
    public AgentSettings Agent { get; }
    public StorageSettings Storage { get; }
    public TelemetrySettings Telemetry { get; }

    private static string Get(System.Collections.Specialized.NameValueCollection s, string key, string def = "")
        => s[key] ?? def;
    private static bool GetBool(System.Collections.Specialized.NameValueCollection s, string key, bool def = false)
        => bool.TryParse(s[key], out var v) ? v : def;
    private static int GetInt(System.Collections.Specialized.NameValueCollection s, string key, int def = 0)
        => int.TryParse(s[key], out var v) ? v : def;
    private static double GetDouble(System.Collections.Specialized.NameValueCollection s, string key, double def = 0)
        => double.TryParse(s[key], System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : def;

    public sealed class AppSettings(System.Collections.Specialized.NameValueCollection s)
    {
        public string Name => Get(s, "App:Name", "SKClaw");
        public string Version => Get(s, "App:Version", "1.0.0");
        public string Environment => Get(s, "App:Environment", "Production");
        public string LogLevel => Get(s, "App:LogLevel", "Information");
    }

    public sealed class LLMSettings(System.Collections.Specialized.NameValueCollection s)
    {
        public string DefaultProvider => Get(s, "LLM:DefaultProvider", "openai");
        public string DefaultModel => Get(s, "LLM:DefaultModel", "gpt-4o");
        public string DefaultEmbeddingModel => Get(s, "LLM:DefaultEmbeddingModel", "text-embedding-3-small");
        public int MaxTokens => GetInt(s, "LLM:MaxTokens", 4096);
        public double Temperature => GetDouble(s, "LLM:Temperature", 0.7);
        public double TopP => GetDouble(s, "LLM:TopP", 1.0);
        public int RequestTimeoutSeconds => GetInt(s, "LLM:RequestTimeoutSeconds", 120);

        public OpenAIConfig OpenAI => new(s);
        public GeminiConfig Gemini => new(s);
        public AnthropicConfig Anthropic => new(s);
        public OllamaConfig Ollama => new(s);
        public OpenAICompatibleConfig OpenAICompatible => new(s);

        public sealed class OpenAIConfig(System.Collections.Specialized.NameValueCollection s)
        {
            public string ApiKey => Get(s, "LLM:OpenAI:ApiKey");
            public string OrgId => Get(s, "LLM:OpenAI:OrgId");
            public string Endpoint => Get(s, "LLM:OpenAI:Endpoint", "https://api.openai.com/v1");
            public string ChatModel => Get(s, "LLM:OpenAI:ChatModel", "gpt-4o");
            public string EmbeddingModel => Get(s, "LLM:OpenAI:EmbeddingModel", "text-embedding-3-small");
            public string ImageModel => Get(s, "LLM:OpenAI:ImageModel", "dall-e-3");
        }

        public sealed class GeminiConfig(System.Collections.Specialized.NameValueCollection s)
        {
            public string ApiKey => Get(s, "LLM:Gemini:ApiKey");
            public string ChatModel => Get(s, "LLM:Gemini:ChatModel", "gemini-1.5-pro");
            public string EmbeddingModel => Get(s, "LLM:Gemini:EmbeddingModel", "text-embedding-004");
            public string Endpoint => Get(s, "LLM:Gemini:Endpoint", "https://generativelanguage.googleapis.com");
        }

        public sealed class AnthropicConfig(System.Collections.Specialized.NameValueCollection s)
        {
            public string ApiKey => Get(s, "LLM:Anthropic:ApiKey");
            public string ChatModel => Get(s, "LLM:Anthropic:ChatModel", "claude-sonnet-4-20250514");
            public string Endpoint => Get(s, "LLM:Anthropic:Endpoint", "https://api.anthropic.com");
            public string Version => Get(s, "LLM:Anthropic:Version", "2023-06-01");
        }

        public sealed class OllamaConfig(System.Collections.Specialized.NameValueCollection s)
        {
            public string Endpoint => Get(s, "LLM:Ollama:Endpoint", "http://localhost:11434");
            public string ChatModel => Get(s, "LLM:Ollama:ChatModel", "llama3.2");
            public string EmbeddingModel => Get(s, "LLM:Ollama:EmbeddingModel", "nomic-embed-text");
        }

        public sealed class OpenAICompatibleConfig(System.Collections.Specialized.NameValueCollection s)
        {
            public string Endpoint => Get(s, "LLM:OpenAICompatible:Endpoint", "http://localhost:1234/v1");
            public string ApiKey => Get(s, "LLM:OpenAICompatible:ApiKey", "lm-studio");
            public string ChatModel => Get(s, "LLM:OpenAICompatible:ChatModel", "local-model");
            public string EmbeddingModel => Get(s, "LLM:OpenAICompatible:EmbeddingModel", "local-embedding");
        }
    }

    public sealed class MemorySettings(System.Collections.Specialized.NameValueCollection s)
    {
        public string Provider => Get(s, "Memory:Provider", "inmemory");
        public string CollectionName => Get(s, "Memory:CollectionName", "skclaw_memory");
        public double RelevanceThreshold => GetDouble(s, "Memory:RelevanceThreshold", 0.7);
        public int MaxResults => GetInt(s, "Memory:MaxResults", 10);

        public SqliteMemoryConfig Sqlite => new(s);
        public QdrantMemoryConfig Qdrant => new(s);
        public ChromaMemoryConfig Chroma => new(s);

        public sealed class SqliteMemoryConfig(System.Collections.Specialized.NameValueCollection s)
        {
            public string ConnectionString => Get(s, "Memory:Sqlite:ConnectionString", "Data Source=skclaw_memory.db");
        }

        public sealed class QdrantMemoryConfig(System.Collections.Specialized.NameValueCollection s)
        {
            public string Endpoint => Get(s, "Memory:Qdrant:Endpoint", "http://localhost:6333");
            public string ApiKey => Get(s, "Memory:Qdrant:ApiKey");
            public int VectorSize => GetInt(s, "Memory:Qdrant:VectorSize", 1536);
        }

        public sealed class ChromaMemoryConfig(System.Collections.Specialized.NameValueCollection s)
        {
            public string Endpoint => Get(s, "Memory:Chroma:Endpoint", "http://localhost:8000");
        }
    }

    public sealed class McpSettings(System.Collections.Specialized.NameValueCollection s)
    {
        public bool Enabled => GetBool(s, "MCP:Enabled", true);
        public string ServerName => Get(s, "MCP:ServerName", "SKClaw MCP Server");
        public string Version => Get(s, "MCP:Version", "1.0.0");
        public string Transport => Get(s, "MCP:Transport", "sse");
        public string SsePath => Get(s, "MCP:SsePath", "/mcp/sse");
        public string MessagePath => Get(s, "MCP:MessagePath", "/mcp/message");
        public string ExternalServers => Get(s, "MCP:ExternalServers");
    }

    public sealed class WebSettings(System.Collections.Specialized.NameValueCollection s)
    {
        public bool Enabled => GetBool(s, "Web:Enabled", true);
        public string Host => Get(s, "Web:Host", "0.0.0.0");
        public int Port => GetInt(s, "Web:Port", 5000);
        public bool UseHttps => GetBool(s, "Web:UseHttps", false);
        public string AllowedOrigins => Get(s, "Web:AllowedOrigins", "*");

        public AdminConfig Admin => new(s);
        public ChatConfig Chat => new(s);
        public ApiConfig Api => new(s);

        public sealed class AdminConfig(System.Collections.Specialized.NameValueCollection s)
        {
            public bool Enabled => GetBool(s, "Web:Admin:Enabled", true);
            public string Path => Get(s, "Web:Admin:Path", "/admin");
            public string Username => Get(s, "Web:Admin:Username", "admin");
            public string Password => Get(s, "Web:Admin:Password", "changeme123");
            public string JwtSecret => Get(s, "Web:Admin:JwtSecret", "change-this-secret");
            public int JwtExpiryHours => GetInt(s, "Web:Admin:JwtExpiryHours", 24);
        }

        public sealed class ChatConfig(System.Collections.Specialized.NameValueCollection s)
        {
            public bool Enabled => GetBool(s, "Web:Chat:Enabled", true);
            public string Path => Get(s, "Web:Chat:Path", "/chat");
            public string Title => Get(s, "Web:Chat:Title", "SKClaw Chat");
            public string WelcomeMessage => Get(s, "Web:Chat:WelcomeMessage", "Halo! Ada yang bisa saya bantu?");
            public bool AllowFileUploads => GetBool(s, "Web:Chat:AllowFileUploads", true);
            public int MaxFileSizeMb => GetInt(s, "Web:Chat:MaxFileSizeMb", 10);
        }

        public sealed class ApiConfig(System.Collections.Specialized.NameValueCollection s)
        {
            public bool Enabled => GetBool(s, "Web:Api:Enabled", true);
            public string Path => Get(s, "Web:Api:Path", "/api");
            public bool RequireAuth => GetBool(s, "Web:Api:RequireAuth", true);
            public int RateLimitPerMinute => GetInt(s, "Web:Api:RateLimitPerMinute", 60);
            public string[] ApiKeys => Get(s, "Web:Api:ApiKeys").Split(',', StringSplitOptions.RemoveEmptyEntries);
        }
    }

    public sealed class ChannelsSettings(System.Collections.Specialized.NameValueCollection s)
    {
        public TelegramConfig Telegram => new(s);
        public DiscordConfig Discord => new(s);
        public SlackConfig Slack => new(s);
        public WhatsAppConfig WhatsApp => new(s);
        public LineConfig Line => new(s);

        public sealed class TelegramConfig(System.Collections.Specialized.NameValueCollection s)
        {
            public bool Enabled => GetBool(s, "Channels:Telegram:Enabled");
            public string BotToken => Get(s, "Channels:Telegram:BotToken");
            public string[] AllowedUsers => Get(s, "Channels:Telegram:AllowedUsers").Split(',', StringSplitOptions.RemoveEmptyEntries);
            public string WebhookUrl => Get(s, "Channels:Telegram:WebhookUrl");
        }

        public sealed class DiscordConfig(System.Collections.Specialized.NameValueCollection s)
        {
            public bool Enabled => GetBool(s, "Channels:Discord:Enabled");
            public string BotToken => Get(s, "Channels:Discord:BotToken");
            public string GuildId => Get(s, "Channels:Discord:GuildId");
            public string[] ChannelIds => Get(s, "Channels:Discord:ChannelIds").Split(',', StringSplitOptions.RemoveEmptyEntries);
            public string CommandPrefix => Get(s, "Channels:Discord:CommandPrefix", "!");
        }

        public sealed class SlackConfig(System.Collections.Specialized.NameValueCollection s)
        {
            public bool Enabled => GetBool(s, "Channels:Slack:Enabled");
            public string BotToken => Get(s, "Channels:Slack:BotToken");
            public string AppToken => Get(s, "Channels:Slack:AppToken");
            public string SigningSecret => Get(s, "Channels:Slack:SigningSecret");
        }

        public sealed class WhatsAppConfig(System.Collections.Specialized.NameValueCollection s)
        {
            public bool Enabled => GetBool(s, "Channels:WhatsApp:Enabled");
            public string AccountSid => Get(s, "Channels:WhatsApp:AccountSid");
            public string AuthToken => Get(s, "Channels:WhatsApp:AuthToken");
            public string FromNumber => Get(s, "Channels:WhatsApp:FromNumber");
        }

        public sealed class LineConfig(System.Collections.Specialized.NameValueCollection s)
        {
            public bool Enabled => GetBool(s, "Channels:Line:Enabled");
            public string ChannelAccessToken => Get(s, "Channels:Line:ChannelAccessToken");
            public string ChannelSecret => Get(s, "Channels:Line:ChannelSecret");
        }
    }

    public sealed class PluginsSettings(System.Collections.Specialized.NameValueCollection s)
    {
        public bool AutoLoad => GetBool(s, "Plugins:AutoLoad", true);
        public string Directory => Get(s, "Plugins:Directory", "./plugins");
        public string[] EnabledSkills => Get(s, "Plugins:EnabledSkills").Split(',', StringSplitOptions.RemoveEmptyEntries);

        public SearchConfig Search => new(s);
        public CodeConfig Code => new(s);
        public EmailConfig Email => new(s);

        public sealed class SearchConfig(System.Collections.Specialized.NameValueCollection s)
        {
            public string Provider => Get(s, "Plugins:Search:Provider", "bing");
            public string BingApiKey => Get(s, "Plugins:Search:BingApiKey");
            public string GoogleApiKey => Get(s, "Plugins:Search:GoogleApiKey");
            public string GoogleCseId => Get(s, "Plugins:Search:GoogleCseId");
            public int MaxResults => GetInt(s, "Plugins:Search:MaxResults", 5);
        }

        public sealed class CodeConfig(System.Collections.Specialized.NameValueCollection s)
        {
            public bool Enabled => GetBool(s, "Plugins:Code:Enabled");
            public string[] AllowedLanguages => Get(s, "Plugins:Code:AllowedLanguages").Split(',', StringSplitOptions.RemoveEmptyEntries);
            public int TimeoutSeconds => GetInt(s, "Plugins:Code:TimeoutSeconds", 30);
            public int MaxOutputBytes => GetInt(s, "Plugins:Code:MaxOutputBytes", 10240);
        }

        public sealed class EmailConfig(System.Collections.Specialized.NameValueCollection s)
        {
            public string SmtpHost => Get(s, "Plugins:Email:SmtpHost");
            public int SmtpPort => GetInt(s, "Plugins:Email:SmtpPort", 587);
            public string SmtpUser => Get(s, "Plugins:Email:SmtpUser");
            public string SmtpPassword => Get(s, "Plugins:Email:SmtpPassword");
            public bool UseSsl => GetBool(s, "Plugins:Email:UseSsl", true);
        }
    }

    public sealed class AgentSettings(System.Collections.Specialized.NameValueCollection s)
    {
        public string Name => Get(s, "Agent:Name", "SKClaw");
        public string Description => Get(s, "Agent:Description", "A powerful AI assistant");
        public string SystemPrompt => Get(s, "Agent:SystemPrompt", "You are SKClaw, a helpful AI assistant.");
        public int MaxIterations => GetInt(s, "Agent:MaxIterations", 10);
        public bool EnablePlanner => GetBool(s, "Agent:EnablePlanner", true);
        public string PlannerType => Get(s, "Agent:PlannerType", "sequential");
        public bool EnableMemory => GetBool(s, "Agent:EnableMemory", true);
        public int MemoryWindowSize => GetInt(s, "Agent:MemoryWindowSize", 20);
        public bool StreamResponse => GetBool(s, "Agent:StreamResponse", true);
    }

    public sealed class StorageSettings(System.Collections.Specialized.NameValueCollection s)
    {
        public string Provider => Get(s, "Storage:Provider", "sqlite");
        public string SqlitePath => Get(s, "Storage:Sqlite:Path", "skclaw.db");
        public string PostgresConnectionString => Get(s, "Storage:Postgres:ConnectionString");
        public string SqlServerConnectionString => Get(s, "Storage:SqlServer:ConnectionString");
    }

    public sealed class TelemetrySettings(System.Collections.Specialized.NameValueCollection s)
    {
        public bool Enabled => GetBool(s, "Telemetry:Enabled");
        public string OtelEndpoint => Get(s, "Telemetry:OtelEndpoint", "http://localhost:4317");
        public string ServiceName => Get(s, "Telemetry:ServiceName", "SKClaw");
    }
}
