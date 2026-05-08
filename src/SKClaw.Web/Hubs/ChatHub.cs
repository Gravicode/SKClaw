using Microsoft.AspNetCore.SignalR;
using SKClaw.Core.Agents;
using SKClaw.Core.Configuration;

namespace SKClaw.Web.Hubs;

/// <summary>
/// SignalR hub for real-time web chat.
/// Clients connect here for streaming responses and live updates.
/// </summary>
public class ChatHub : Hub
{
    private readonly SkClawAgent _agent;
    private readonly AppConfiguration _config;
    private readonly ILogger<ChatHub> _logger;

    // Session tracking: connectionId -> sessionId
    private static readonly Dictionary<string, string> _sessions = new();

    public ChatHub(SkClawAgent agent, AppConfiguration config, ILogger<ChatHub> logger)
    {
        _agent = agent;
        _config = config;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var sessionId = Context.ConnectionId;
        _sessions[sessionId] = sessionId;

        await Clients.Caller.SendAsync("Connected", new
        {
            sessionId,
            agentName = _config.Agent.Name,
            welcomeMessage = _config.Web.Chat.WelcomeMessage,
            provider = _config.LLM.DefaultProvider,
            model = _config.LLM.DefaultModel
        });

        _logger.LogInformation("Chat client connected: {SessionId}", sessionId[..8]);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _sessions.Remove(Context.ConnectionId);
        _logger.LogInformation("Chat client disconnected: {SessionId}", Context.ConnectionId[..8]);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Send a message and receive streaming response.
    /// </summary>
    public async Task SendMessage(string message, string? sessionId = null)
    {
        var connId = Context.ConnectionId;
        var sid = sessionId ?? connId;

        _logger.LogDebug("Hub message [{Sid}]: {Msg}", sid[..8], message[..Math.Min(80, message.Length)]);

        // Notify client that thinking has started
        await Clients.Caller.SendAsync("Thinking", true);

        try
        {
            if (_config.Agent.StreamResponse)
            {
                // Stream response token by token
                await Clients.Caller.SendAsync("StreamStart", new { sessionId = sid });

                await foreach (var chunk in _agent.ChatStreamAsync(message))
                {
                    await Clients.Caller.SendAsync("StreamChunk", new { chunk, sessionId = sid });
                }

                await Clients.Caller.SendAsync("StreamEnd", new { sessionId = sid });
            }
            else
            {
                var response = await _agent.ChatAsync(message, sid);
                await Clients.Caller.SendAsync("ReceiveMessage", new
                {
                    content = response.Content,
                    role = response.Role,
                    sessionId = sid,
                    timestamp = response.Timestamp
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hub error for session {Sid}", sid);
            await Clients.Caller.SendAsync("Error", new { message = ex.Message });
        }
        finally
        {
            await Clients.Caller.SendAsync("Thinking", false);
        }
    }

    /// <summary>
    /// Reset the conversation for this session.
    /// </summary>
    public async Task ResetConversation()
    {
        _agent.ResetConversation();
        await Clients.Caller.SendAsync("ConversationReset", new
        {
            message = "Conversation reset successfully.",
            welcomeMessage = _config.Web.Chat.WelcomeMessage
        });
    }

    /// <summary>
    /// Get conversation history.
    /// </summary>
    public async Task GetHistory()
    {
        var history = _agent.GetHistory()
            .Select(m => new { role = m.Role.Label, content = m.Content ?? "" })
            .ToList();
        await Clients.Caller.SendAsync("History", history);
    }

    /// <summary>
    /// Ping to keep connection alive.
    /// </summary>
    public Task Ping() => Clients.Caller.SendAsync("Pong", DateTimeOffset.UtcNow);
}
