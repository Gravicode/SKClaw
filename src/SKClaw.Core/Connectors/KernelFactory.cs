using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Memory;
using SKClaw.Core.Configuration;

namespace SKClaw.Core.Connectors;

/// <summary>
/// Factory that creates Semantic Kernel instances configured for
/// any supported LLM provider based on app.config settings.
/// Supported: OpenAI, Gemini, Anthropic, Ollama, OpenAI-Compatible
/// </summary>
public class KernelFactory
{
    private readonly AppConfiguration _config;
    private readonly ILoggerFactory _loggerFactory;

    public KernelFactory(AppConfiguration config, ILoggerFactory loggerFactory)
    {
        _config = config;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Creates a Kernel for the default provider specified in config.
    /// </summary>
    public Kernel CreateKernel(string? providerOverride = null)
    {
        var provider = (providerOverride ?? _config.LLM.DefaultProvider).ToLowerInvariant();
        return provider switch
        {
            "openai" => CreateOpenAIKernel(),
            "gemini" => CreateGeminiKernel(),
            "anthropic" => CreateAnthropicKernel(),
            "ollama" => CreateOllamaKernel(),
            "openai-compatible" or "openai_compatible" or "compatible" => CreateOpenAICompatibleKernel(),
            _ => throw new InvalidOperationException($"Unknown LLM provider: '{provider}'. " +
                 "Supported: openai, gemini, anthropic, ollama, openai-compatible")
        };
    }

    private Kernel CreateOpenAIKernel()
    {
        var cfg = _config.LLM.OpenAI;
        var builder = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(
                modelId: cfg.ChatModel,
                apiKey: cfg.ApiKey,
                orgId: string.IsNullOrEmpty(cfg.OrgId) ? null : cfg.OrgId,
                serviceId: "chat")
            .AddOpenAITextEmbeddingGeneration(
                modelId: cfg.EmbeddingModel,
                apiKey: cfg.ApiKey,
                serviceId: "embedding");

        builder.Services.AddLogging(l => l.AddConsole().SetMinimumLevel(ParseLogLevel(_config.App.LogLevel)));
        return builder.Build();
    }

    private Kernel CreateGeminiKernel()
    {
        var cfg = _config.LLM.Gemini;
        var builder = Kernel.CreateBuilder();

        // Gemini via Google AI connector
#pragma warning disable SKEXP0070 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        builder.AddGoogleAIGeminiChatCompletion(
            modelId: cfg.ChatModel,
            apiKey: cfg.ApiKey,
            serviceId: "chat");

        builder.AddGoogleAIEmbeddingGeneration(
            modelId: cfg.EmbeddingModel,
            apiKey: cfg.ApiKey,
            serviceId: "embedding");
#pragma warning restore SKEXP0070 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

        builder.Services.AddLogging(l => l.AddConsole().SetMinimumLevel(ParseLogLevel(_config.App.LogLevel)));
        return builder.Build();
    }

    private Kernel CreateAnthropicKernel()
    {
        var cfg = _config.LLM.Anthropic;

        // Anthropic via OpenAI-compatible endpoint with Anthropic headers
        // Uses a custom HttpClient with Anthropic-specific headers
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(cfg.Endpoint),
            Timeout = TimeSpan.FromSeconds(_config.LLM.RequestTimeoutSeconds)
        };
        httpClient.DefaultRequestHeaders.Add("x-api-key", cfg.ApiKey);
        httpClient.DefaultRequestHeaders.Add("anthropic-version", cfg.Version);

        var builder = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(
                modelId: cfg.ChatModel,
                apiKey: cfg.ApiKey,
                httpClient: httpClient,
                serviceId: "chat");

        builder.Services.AddLogging(l => l.AddConsole().SetMinimumLevel(ParseLogLevel(_config.App.LogLevel)));
        return builder.Build();
    }

    private Kernel CreateOllamaKernel()
    {
        var cfg = _config.LLM.Ollama;
#pragma warning disable SKEXP0070 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        var builder = Kernel.CreateBuilder()
            .AddOllamaChatCompletion(
                modelId: cfg.ChatModel,
                endpoint: new Uri(cfg.Endpoint),
                serviceId: "chat")
            .AddOllamaTextEmbeddingGeneration(
                modelId: cfg.EmbeddingModel,
                endpoint: new Uri(cfg.Endpoint),
                serviceId: "embedding");
#pragma warning restore SKEXP0070 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

        builder.Services.AddLogging(l => l.AddConsole().SetMinimumLevel(ParseLogLevel(_config.App.LogLevel)));
        return builder.Build();
    }

    private Kernel CreateOpenAICompatibleKernel()
    {
        var cfg = _config.LLM.OpenAICompatible;

        // OpenAI-compatible APIs (LM Studio, Together AI, Groq, etc.)
        var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(_config.LLM.RequestTimeoutSeconds)
        };

        var builder = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(
                modelId: cfg.ChatModel,
                apiKey: cfg.ApiKey,
                endpoint: new Uri(cfg.Endpoint),
                httpClient: httpClient,
                serviceId: "chat")
            .AddOpenAITextEmbeddingGeneration(
                modelId: cfg.EmbeddingModel,
                apiKey: cfg.ApiKey, 
                //endpoint: new Uri(cfg.Endpoint),
                httpClient: httpClient,
                serviceId: "embedding");

        builder.Services.AddLogging(l => l.AddConsole().SetMinimumLevel(ParseLogLevel(_config.App.LogLevel)));
        return builder.Build();
    }

    private static Microsoft.Extensions.Logging.LogLevel ParseLogLevel(string level) =>
        level.ToLowerInvariant() switch
        {
            "debug" => Microsoft.Extensions.Logging.LogLevel.Debug,
            "information" or "info" => Microsoft.Extensions.Logging.LogLevel.Information,
            "warning" or "warn" => Microsoft.Extensions.Logging.LogLevel.Warning,
            "error" => Microsoft.Extensions.Logging.LogLevel.Error,
            "critical" => Microsoft.Extensions.Logging.LogLevel.Critical,
            "none" => Microsoft.Extensions.Logging.LogLevel.None,
            _ => Microsoft.Extensions.Logging.LogLevel.Information
        };
}
