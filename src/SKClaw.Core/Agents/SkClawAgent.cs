using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using SKClaw.Core.Configuration;
using SKClaw.Core.Memory;
using SKClaw.Core.Models;

namespace SKClaw.Core.Agents;

/// <summary>
/// SKClaw Agent - the core AI orchestrator.
/// Handles conversation, tool use, planning, and memory.
/// </summary>
public class SkClawAgent
{
    private readonly Kernel _kernel;
    private readonly AppConfiguration _config;
    private readonly ILogger<SkClawAgent> _logger;
    private readonly SkClawMemory? _memory;
    private readonly ChatHistory _chatHistory;

    public string AgentId { get; } = Guid.NewGuid().ToString("N")[..8];
    public string Name => _config.Agent.Name;

    public SkClawAgent(
        Kernel kernel,
        AppConfiguration config,
        ILogger<SkClawAgent> logger,
        SkClawMemory? memory = null)
    {
        _kernel = kernel;
        _config = config;
        _logger = logger;
        _memory = memory;
        _chatHistory = new ChatHistory(_config.Agent.SystemPrompt);
    }

    /// <summary>
    /// Process a user message and return the agent response.
    /// Supports streaming via IAsyncEnumerable.
    /// </summary>
    public async Task<AgentResponse> ChatAsync(string userMessage, string? sessionId = null,
        CancellationToken ct = default)
    {
        _logger.LogDebug("[Agent:{Id}] Processing: {Msg}", AgentId, userMessage[..Math.Min(80, userMessage.Length)]);

        // Inject memory context if enabled
        if (_config.Agent.EnableMemory && _memory != null)
        {
            var memContext = await _memory.RecallAsync(userMessage, ct: ct);
            if (!string.IsNullOrEmpty(memContext))
            {
                _chatHistory.AddSystemMessage($"[Memory Context]\n{memContext}");
            }
        }

        _chatHistory.AddUserMessage(userMessage);

        // Trim history to window size
        TrimHistory();

        var settings = new OpenAIPromptExecutionSettings
        {
            MaxTokens = _config.LLM.MaxTokens,
            Temperature = _config.LLM.Temperature,
            TopP = _config.LLM.TopP,
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
        };

        try
        {
            var chatService = _kernel.GetRequiredService<IChatCompletionService>();
            var result = await chatService.GetChatMessageContentAsync(
                _chatHistory, settings, _kernel, ct);

            var responseText = result.Content ?? "";
            _chatHistory.AddAssistantMessage(responseText);

            // Store to memory
            if (_config.Agent.EnableMemory && _memory != null)
            {
                await _memory.RememberAsync(userMessage, responseText, ct: ct);
            }

            return new AgentResponse
            {
                SessionId = sessionId ?? AgentId,
                Content = responseText,
                Role = "assistant",
                Timestamp = DateTimeOffset.UtcNow,
                TokensUsed = result.Metadata?.TryGetValue("Usage", out var usage) == true
                    ? usage?.ToString() : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent error processing message");
            return new AgentResponse
            {
                SessionId = sessionId ?? AgentId,
                Content = $"Error: {ex.Message}",
                Role = "error",
                Timestamp = DateTimeOffset.UtcNow
            };
        }
    }

    /// <summary>
    /// Stream a response token by token.
    /// </summary>
    public async IAsyncEnumerable<string> ChatStreamAsync(string userMessage,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_config.Agent.EnableMemory && _memory != null)
        {
            var memContext = await _memory.RecallAsync(userMessage, ct: ct);
            if (!string.IsNullOrEmpty(memContext))
                _chatHistory.AddSystemMessage($"[Memory Context]\n{memContext}");
        }

        _chatHistory.AddUserMessage(userMessage);
        TrimHistory();

        var settings = new OpenAIPromptExecutionSettings
        {
            MaxTokens = _config.LLM.MaxTokens,
            Temperature = _config.LLM.Temperature,
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
        };

        var chatService = _kernel.GetRequiredService<IChatCompletionService>();
        var sb = new System.Text.StringBuilder();

        await foreach (var chunk in chatService.GetStreamingChatMessageContentsAsync(
            _chatHistory, settings, _kernel, ct))
        {
            var text = chunk.Content ?? "";
            if (!string.IsNullOrEmpty(text))
            {
                sb.Append(text);
                yield return text;
            }
        }

        var fullResponse = sb.ToString();
        _chatHistory.AddAssistantMessage(fullResponse);

        if (_config.Agent.EnableMemory && _memory != null)
            await _memory.RememberAsync(userMessage, fullResponse, ct: ct);
    }

    /// <summary>
    /// Reset the conversation history.
    /// </summary>
    public void ResetConversation()
    {
        _chatHistory.Clear();
        _chatHistory.AddSystemMessage(_config.Agent.SystemPrompt);
        _logger.LogInformation("[Agent:{Id}] Conversation reset", AgentId);
    }

    /// <summary>
    /// Get the current conversation history.
    /// </summary>
    public IReadOnlyList<ChatMessageContent> GetHistory() => _chatHistory.AsReadOnly();

    private void TrimHistory()
    {
        var maxPairs = _config.Agent.MemoryWindowSize;
        // Keep system message + last N user/assistant pairs
        var nonSystem = _chatHistory.Where(m => m.Role != AuthorRole.System).ToList();
        if (nonSystem.Count > maxPairs * 2)
        {
            var toRemove = nonSystem.Take(nonSystem.Count - maxPairs * 2).ToList();
            foreach (var msg in toRemove)
                _chatHistory.Remove(msg);
        }
    }
}
