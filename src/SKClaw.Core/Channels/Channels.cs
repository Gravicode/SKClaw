using Microsoft.Extensions.Logging;
using SKClaw.Core.Agents;
using SKClaw.Core.Configuration;
using System.Net.Http.Json;
using System.Text.Json;

namespace SKClaw.Core.Channels;

/// <summary>
/// Base interface for all messaging channels.
/// </summary>
public interface IChannel
{
    string Name { get; }
    bool IsEnabled { get; }
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    Task SendMessageAsync(string chatId, string message, CancellationToken ct = default);
}

/// <summary>
/// Channel Manager - starts/stops all enabled channels.
/// </summary>
public class ChannelManager
{
    private readonly List<IChannel> _channels = [];
    private readonly ILogger<ChannelManager> _logger;

    public ChannelManager(IEnumerable<IChannel> channels, ILogger<ChannelManager> logger)
    {
        _channels.AddRange(channels.Where(c => c.IsEnabled));
        _logger = logger;
    }

    public async Task StartAllAsync(CancellationToken ct = default)
    {
        foreach (var ch in _channels)
        {
            try
            {
                _logger.LogInformation("Starting channel: {Channel}", ch.Name);
                await ch.StartAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start channel: {Channel}", ch.Name);
            }
        }
    }

    public async Task StopAllAsync()
    {
        foreach (var ch in _channels)
        {
            try { await ch.StopAsync(); }
            catch (Exception ex) { _logger.LogError(ex, "Error stopping {Channel}", ch.Name); }
        }
    }

    public IReadOnlyList<string> GetActiveChannels() =>
        _channels.Select(c => c.Name).ToList();
}

// ─────────────────────────────────────────────────────────────
// TELEGRAM CHANNEL
// ─────────────────────────────────────────────────────────────
public class TelegramChannel : IChannel
{
    public string Name => "Telegram";
    public bool IsEnabled => _config.Channels.Telegram.Enabled;

    private readonly AppConfiguration _config;
    private readonly SkClawAgent _agent;
    private readonly ILogger<TelegramChannel> _logger;
    private static readonly HttpClient _http = new();
    private CancellationTokenSource? _cts;
    private long _lastUpdateId = 0;

    private string ApiUrl => $"https://api.telegram.org/bot{_config.Channels.Telegram.BotToken}";

    public TelegramChannel(AppConfiguration config, SkClawAgent agent, ILogger<TelegramChannel> logger)
    {
        _config = config;
        _agent = agent;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Set webhook or start polling
        if (!string.IsNullOrEmpty(_config.Channels.Telegram.WebhookUrl))
        {
            await SetWebhookAsync(_config.Channels.Telegram.WebhookUrl, ct);
            _logger.LogInformation("Telegram webhook set: {Url}", _config.Channels.Telegram.WebhookUrl);
        }
        else
        {
            _logger.LogInformation("Telegram polling started");
            _ = PollAsync(_cts.Token);
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        _cts?.Cancel();
        _logger.LogInformation("Telegram channel stopped");
        await Task.CompletedTask;
    }

    public async Task SendMessageAsync(string chatId, string message, CancellationToken ct = default)
    {
        var payload = new { chat_id = chatId, text = message, parse_mode = "Markdown" };
        await _http.PostAsJsonAsync($"{ApiUrl}/sendMessage", payload, ct);
    }

    private async Task PollAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var url = $"{ApiUrl}/getUpdates?offset={_lastUpdateId + 1}&timeout=30";
                var res = await _http.GetFromJsonAsync<TelegramUpdatesResponse>(url, ct);
                if (res?.Result == null) continue;

                foreach (var update in res.Result)
                {
                    _lastUpdateId = update.UpdateId;
                    if (update.Message?.Text != null)
                        await HandleMessageAsync(update.Message, ct);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Telegram poll error");
                await Task.Delay(5000, ct);
            }
        }
    }

    private async Task HandleMessageAsync(TelegramMessage msg, CancellationToken ct)
    {
        var chatId = msg.Chat.Id.ToString();
        var allowed = _config.Channels.Telegram.AllowedUsers;
        if (allowed.Length > 0 && !allowed.Contains(msg.From?.Username ?? ""))
        {
            await SendMessageAsync(chatId, "❌ You are not authorized to use this bot.", ct);
            return;
        }

        _logger.LogInformation("Telegram [{User}]: {Msg}", msg.From?.Username, msg.Text?[..Math.Min(80, msg.Text.Length)]);

        // Typing indicator
        await _http.PostAsJsonAsync($"{ApiUrl}/sendChatAction",
            new { chat_id = chatId, action = "typing" }, ct);

        var response = await _agent.ChatAsync(msg.Text!, chatId, ct);
        await SendMessageAsync(chatId, response.Content, ct);
    }

    private async Task SetWebhookAsync(string webhookUrl, CancellationToken ct)
    {
        await _http.PostAsJsonAsync($"{ApiUrl}/setWebhook", new { url = webhookUrl }, ct);
    }

    // DTO models
    private record TelegramUpdatesResponse(bool Ok, List<TelegramUpdate> Result);
    private record TelegramUpdate(long UpdateId, TelegramMessage? Message);
    private record TelegramMessage(string? Text, TelegramChat Chat, TelegramUser? From);
    private record TelegramChat(long Id);
    private record TelegramUser(string? Username, string? FirstName);
}

// ─────────────────────────────────────────────────────────────
// DISCORD CHANNEL
// ─────────────────────────────────────────────────────────────
public class DiscordChannel : IChannel
{
    public string Name => "Discord";
    public bool IsEnabled => _config.Channels.Discord.Enabled;

    private readonly AppConfiguration _config;
    private readonly SkClawAgent _agent;
    private readonly ILogger<DiscordChannel> _logger;
    private static readonly HttpClient _http = new();

    private string ApiUrl => "https://discord.com/api/v10";

    public DiscordChannel(AppConfiguration config, SkClawAgent agent, ILogger<DiscordChannel> logger)
    {
        _config = config;
        _agent = agent;
        _logger = logger;
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bot", config.Channels.Discord.BotToken);
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        // Discord requires Gateway WebSocket - simplified implementation
        // Production would use Discord.Net or DiscordSocketClient
        _logger.LogInformation("Discord channel initialized. Webhook mode active.");
        _logger.LogWarning("Full Discord Gateway requires Discord.Net package. Current mode: Webhook only.");
        await Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Discord channel stopped");
        await Task.CompletedTask;
    }

    /// <summary>
    /// Handle incoming webhook from Discord (called by web controller).
    /// </summary>
    public async Task HandleInteractionAsync(DiscordInteraction interaction, CancellationToken ct = default)
    {
        if (interaction.Type == 2) // ApplicationCommand
        {
            var message = interaction.Data?.Options?.FirstOrDefault()?.Value ?? "";
            var channelId = interaction.ChannelId;

            if (!IsAllowedChannel(channelId)) return;

            _logger.LogInformation("Discord [{Guild}#{Channel}]: {Msg}",
                interaction.GuildId, channelId, message[..Math.Min(80, message.Length)]);

            var response = await _agent.ChatAsync(message, $"discord_{channelId}", ct);
            await SendMessageAsync(channelId, response.Content, ct);
        }
    }

    public async Task SendMessageAsync(string channelId, string message, CancellationToken ct = default)
    {
        // Split message if too long (Discord limit: 2000 chars)
        if (message.Length <= 2000)
        {
            await _http.PostAsJsonAsync($"{ApiUrl}/channels/{channelId}/messages",
                new { content = message }, ct);
        }
        else
        {
            for (int i = 0; i < message.Length; i += 2000)
            {
                var chunk = message[i..Math.Min(i + 2000, message.Length)];
                await _http.PostAsJsonAsync($"{ApiUrl}/channels/{channelId}/messages",
                    new { content = chunk }, ct);
                await Task.Delay(500, ct);
            }
        }
    }

    private bool IsAllowedChannel(string channelId)
    {
        var allowed = _config.Channels.Discord.ChannelIds;
        return allowed.Length == 0 || allowed.Contains(channelId);
    }

    public record DiscordInteraction(int Type, string ChannelId, string? GuildId, DiscordInteractionData? Data);
    public record DiscordInteractionData(string Name, List<DiscordOption>? Options);
    public record DiscordOption(string Name, string Value);
}

// ─────────────────────────────────────────────────────────────
// SLACK CHANNEL
// ─────────────────────────────────────────────────────────────
public class SlackChannel : IChannel
{
    public string Name => "Slack";
    public bool IsEnabled => _config.Channels.Slack.Enabled;

    private readonly AppConfiguration _config;
    private readonly SkClawAgent _agent;
    private readonly ILogger<SlackChannel> _logger;
    private static readonly HttpClient _http = new();

    public SlackChannel(AppConfiguration config, SkClawAgent agent, ILogger<SlackChannel> logger)
    {
        _config = config;
        _agent = agent;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Slack channel initialized (Events API mode)");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>Handle Slack Events API payload (called by web controller).</summary>
    public async Task<string> HandleEventAsync(JsonElement payload, CancellationToken ct = default)
    {
        // URL verification challenge
        if (payload.TryGetProperty("challenge", out var challenge))
            return challenge.GetString() ?? "";

        if (!payload.TryGetProperty("event", out var ev)) return "";

        var type = ev.GetProperty("type").GetString();
        if (type != "app_mention" && type != "message") return "";

        // Ignore bot messages to prevent loops
        if (ev.TryGetProperty("bot_id", out _)) return "";

        var text = ev.GetProperty("text").GetString() ?? "";
        var channelId = ev.GetProperty("channel").GetString() ?? "";
        var userId = ev.TryGetProperty("user", out var u) ? u.GetString() : "";

        // Remove @mention prefix
        text = System.Text.RegularExpressions.Regex.Replace(text, @"<@\w+>", "").Trim();
        if (string.IsNullOrEmpty(text)) return "";

        _logger.LogInformation("Slack [{Channel}] {User}: {Msg}", channelId, userId, text[..Math.Min(80, text.Length)]);

        var response = await _agent.ChatAsync(text, $"slack_{channelId}", ct);
        await SendMessageAsync(channelId, response.Content, ct);

        return "";
    }

    public async Task SendMessageAsync(string channelId, string message, CancellationToken ct = default)
    {
        var payload = new { channel = channelId, text = message };
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://slack.com/api/chat.postMessage")
        {
            Content = JsonContent.Create(payload),
            Headers = { Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", _config.Channels.Slack.BotToken) }
        };
        await _http.SendAsync(req, ct);
    }

    /// <summary>Verify Slack request signature.</summary>
    public bool VerifySignature(string signature, string timestamp, string body)
    {
        var baseString = $"v0:{timestamp}:{body}";
        using var hmac = new System.Security.Cryptography.HMACSHA256(
            System.Text.Encoding.UTF8.GetBytes(_config.Channels.Slack.SigningSecret));
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(baseString));
        var computed = "v0=" + Convert.ToHexString(hash).ToLower();
        return computed == signature;
    }
}

// ─────────────────────────────────────────────────────────────
// WHATSAPP CHANNEL (via Twilio)
// ─────────────────────────────────────────────────────────────
public class WhatsAppChannel : IChannel
{
    public string Name => "WhatsApp";
    public bool IsEnabled => _config.Channels.WhatsApp.Enabled;

    private readonly AppConfiguration _config;
    private readonly SkClawAgent _agent;
    private readonly ILogger<WhatsAppChannel> _logger;
    private static readonly HttpClient _http = new();

    public WhatsAppChannel(AppConfiguration config, SkClawAgent agent, ILogger<WhatsAppChannel> logger)
    {
        _config = config;
        _agent = agent;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("WhatsApp channel initialized (Twilio webhook mode)");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async Task HandleWebhookAsync(string from, string body, CancellationToken ct = default)
    {
        _logger.LogInformation("WhatsApp [{From}]: {Msg}", from, body[..Math.Min(80, body.Length)]);
        var response = await _agent.ChatAsync(body, $"wa_{from}", ct);
        await SendMessageAsync(from, response.Content, ct);
    }

    public async Task SendMessageAsync(string to, string message, CancellationToken ct = default)
    {
        var cfg = _config.Channels.WhatsApp;
        var url = $"https://api.twilio.com/2010-04-01/Accounts/{cfg.AccountSid}/Messages.json";

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("To", to.StartsWith("whatsapp:") ? to : $"whatsapp:{to}"),
            new KeyValuePair<string, string>("From", cfg.FromNumber),
            new KeyValuePair<string, string>("Body", message)
        });

        var authBytes = System.Text.Encoding.ASCII.GetBytes($"{cfg.AccountSid}:{cfg.AuthToken}");
        var authHeader = Convert.ToBase64String(authBytes);

        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authHeader);
        await _http.SendAsync(req, ct);
    }
}
