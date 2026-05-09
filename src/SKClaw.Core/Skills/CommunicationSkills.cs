using System.ComponentModel;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Linq;
using System.Net.Http.Json;
using Microsoft.SemanticKernel;
using SKClaw.Core.Configuration;

namespace SKClaw.Core.Skills;

/// <summary>
/// EmailSkill — Full SMTP email sending, templates, and bulk operations.
/// </summary>
public class EmailSkill
{
    private readonly AppConfiguration _config;
    public EmailSkill(AppConfiguration config) => _config = config;

    [KernelFunction, Description("Send a plain text or HTML email via SMTP")]
    public async Task<string> SendEmailAsync(
        [Description("Recipient email address(es), comma-separated")] string to,
        [Description("Email subject")] string subject,
        [Description("Email body (plain text or HTML)")] string body,
        [Description("CC addresses, comma-separated (optional)")] string cc = "",
        [Description("BCC addresses, comma-separated (optional)")] string bcc = "",
        [Description("Is the body HTML? true/false")] bool isHtml = false,
        [Description("Reply-to address (optional)")] string replyTo = "")
    {
        var smtp = _config.Plugins.Email;
        if (string.IsNullOrEmpty(smtp.SmtpHost))
            return "SMTP not configured. Set Plugins:Email:SmtpHost in app.config.";

        try
        {
            using var client = new SmtpClient(smtp.SmtpHost, smtp.SmtpPort)
            {
                EnableSsl = smtp.UseSsl,
                Credentials = new NetworkCredential(smtp.SmtpUser, smtp.SmtpPassword),
                Timeout = 30000
            };

            var mail = new MailMessage
            {
                From = new MailAddress(smtp.SmtpUser),
                Subject = subject,
                Body = body,
                IsBodyHtml = isHtml,
                BodyEncoding = Encoding.UTF8
            };

            foreach (var addr in to.Split(',').Select(a => a.Trim()).Where(a => a.Length > 0))
                mail.To.Add(addr);
            foreach (var addr in cc.Split(',').Select(a => a.Trim()).Where(a => a.Length > 0))
                mail.CC.Add(addr);
            foreach (var addr in bcc.Split(',').Select(a => a.Trim()).Where(a => a.Length > 0))
                mail.Bcc.Add(addr);
            if (!string.IsNullOrEmpty(replyTo))
                mail.ReplyToList.Add(replyTo);

            await client.SendMailAsync(mail);
            return $"✅ Email sent to {to} | Subject: {subject}";
        }
        catch (Exception ex) { return $"❌ Email failed: {ex.Message}"; }
    }

    [KernelFunction, Description("Send an email using a named template")]
    public async Task<string> SendTemplateEmailAsync(
        [Description("Recipient email")] string to,
        [Description("Template: welcome, password_reset, notification, invoice, meeting_invite")] string template,
        [Description("Template variables as JSON, e.g. {\"name\":\"Alice\",\"company\":\"Acme\"}")] string variables = "{}")
    {
        var vars = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(variables) ?? new Dictionary<string, string>();
        var (subject, body) = template.ToLower() switch
        {
            "welcome" => (
                $"Welcome to {Get(vars, "company", "our platform")}, {Get(vars, "name", "there")}!",
                $"<h2>Welcome, {Get(vars, "name", "there")}!</h2><p>We're thrilled to have you on board at <b>{Get(vars, "company", "our platform")}</b>.</p>"),
            "notification" => (
                Get(vars, "subject", "You have a new notification"),
                $"<h3>{Get(vars, "title", "Notification")}</h3><p>{Get(vars, "message", "")}</p>"),
            "password_reset" => (
                "Password Reset Request",
                $"<p>Hi {Get(vars, "name", "there")},</p><p>Click the link below to reset your password:</p><p><a href='{Get(vars, "link", "#")}'>Reset Password</a></p><p>This link expires in {Get(vars, "expiry", "24 hours")}.</p>"),
            "meeting_invite" => (
                $"Meeting Invitation: {Get(vars, "title", "Meeting")}",
                $"<p>You are invited to: <b>{Get(vars, "title", "Meeting")}</b></p><p>Date: {Get(vars, "date", "TBD")}</p><p>Time: {Get(vars, "time", "TBD")}</p><p>Location: {Get(vars, "location", "TBD")}</p>"),
            _ => (
                Get(vars, "subject", "Message from SKClaw"),
                Get(vars, "body", "No content provided."))
        };

        return await SendEmailAsync(to, subject, body, isHtml: true);
    }

    [KernelFunction, Description("Validate whether an email address is syntactically correct")]
    public string ValidateEmail([Description("Email address to validate")] string email)
    {
        try
        {
            var addr = new MailAddress(email);
            return addr.Address == email ? $"✅ Valid: {email}" : $"✅ Valid (normalized): {addr.Address}";
        }
        catch { return $"❌ Invalid email: {email}"; }
    }

    private static string Get(Dictionary<string, string> d, string k, string def) =>
        d.TryGetValue(k, out var v) ? v : def;
}

/// <summary>
/// NotificationSkill — Push notifications via various channels.
/// </summary>
public class NotificationSkill
{
    private static readonly HttpClient _http = new();

    [KernelFunction, Description("Send a Telegram message to a chat ID using a bot token")]
    public async Task<string> SendTelegramAsync(
        [Description("Telegram Bot Token")] string botToken,
        [Description("Chat ID (user, group, or channel)")] string chatId,
        [Description("Message text (supports Markdown)")] string message,
        [Description("Parse mode: Markdown, HTML, or empty")] string parseMode = "Markdown")
    {
        var url = $"https://api.telegram.org/bot{botToken}/sendMessage";
        var payload = new
        {
            chat_id = chatId,
            text = message,
            parse_mode = string.IsNullOrEmpty(parseMode) ? null : parseMode
        };
        var res = await _http.PostAsJsonAsync(url, payload);
        return res.IsSuccessStatusCode ? "✅ Telegram message sent" : $"❌ Failed: {await res.Content.ReadAsStringAsync()}";
    }

    [KernelFunction, Description("Send a Slack message to a channel using a bot token or webhook")]
    public async Task<string> SendSlackAsync(
        [Description("Slack Bot Token (xoxb-...) OR Webhook URL (https://hooks.slack.com/...)")] string tokenOrWebhook,
        [Description("Channel name or ID (for bot token, e.g. #general)")] string channel,
        [Description("Message text")] string message,
        [Description("Optional thread timestamp to reply to")] string threadTs = "")
    {
        if (tokenOrWebhook.StartsWith("https://"))
        {
            var whPayload = new { text = message, channel };
            var res = await _http.PostAsJsonAsync(tokenOrWebhook, whPayload);
            return res.IsSuccessStatusCode ? "✅ Slack webhook sent" : $"❌ Failed: {res.StatusCode}";
        }
        else
        {
            var payload = new Dictionary<string, object> { ["channel"] = channel, ["text"] = message };
            if (!string.IsNullOrEmpty(threadTs)) payload["thread_ts"] = threadTs;
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://slack.com/api/chat.postMessage")
            {
                Content = System.Net.Http.Json.JsonContent.Create(payload),
                Headers = { Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenOrWebhook) }
            };
            var res = await _http.SendAsync(req);
            return res.IsSuccessStatusCode ? "✅ Slack message sent" : $"❌ Failed: {res.StatusCode}";
        }
    }

    [KernelFunction, Description("Send a Discord message via webhook URL")]
    public async Task<string> SendDiscordAsync(
        [Description("Discord Webhook URL")] string webhookUrl,
        [Description("Message content")] string message,
        [Description("Embed title (optional)")] string embedTitle = "",
        [Description("Embed color as hex e.g. #7289da (optional)")] string embedColor = "")
    {
        object payload;
        if (!string.IsNullOrEmpty(embedTitle))
        {
            int color = embedColor.StartsWith("#")
                ? Convert.ToInt32(embedColor.TrimStart('#'), 16) : 7506394;
            payload = new { embeds = new[] { new { title = embedTitle, description = message, color } } };
        }
        else payload = new { content = message };

        var res = await _http.PostAsJsonAsync(webhookUrl, payload);
        return res.IsSuccessStatusCode ? "✅ Discord message sent" : $"❌ Failed: {res.StatusCode}";
    }

    [KernelFunction, Description("Send a push notification via ntfy.sh (free, no account needed)")]
    public async Task<string> SendNtfyAsync(
        [Description("ntfy topic name (used as endpoint)")] string topic,
        [Description("Notification message")] string message,
        [Description("Title")] string title = "SKClaw",
        [Description("Priority: min, low, default, high, urgent")] string priority = "default",
        [Description("ntfy server URL")] string server = "https://ntfy.sh")
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{server}/{topic}")
        {
            Content = new StringContent(message)
        };
        req.Headers.TryAddWithoutValidation("Title", title);
        req.Headers.TryAddWithoutValidation("Priority", priority);
        var res = await _http.SendAsync(req);
        return res.IsSuccessStatusCode
            ? $"✅ Notification sent to ntfy.sh/{topic}"
            : $"❌ Failed: {res.StatusCode}";
    }
}

/// <summary>
/// SearchSkill — Web search integrations: Bing, Google, DuckDuckGo, Wikipedia.
/// </summary>
public class SearchSkill
{
    private readonly AppConfiguration _config;
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public SearchSkill(AppConfiguration config) => _config = config;

    [KernelFunction, Description("Search the web using the configured search provider (Bing, Google, or DuckDuckGo)")]
    public async Task<string> SearchWebAsync(
        [Description("Search query")] string query,
        [Description("Number of results (1-10)")] int numResults = 5,
        [Description("Provider override: bing, google, duckduckgo (empty = use config)")] string provider = "")
    {
        numResults = Math.Clamp(numResults, 1, 10);
        var prov = string.IsNullOrEmpty(provider) ? _config.Plugins.Search.Provider.ToLower() : provider.ToLower();
        return prov switch
        {
            "bing"       => await SearchBingAsync(query, numResults),
            "google"     => await SearchGoogleAsync(query, numResults),
            "duckduckgo" => await SearchDuckDuckGoAsync(query),
            _            => await SearchDuckDuckGoAsync(query)
        };
    }

    [KernelFunction, Description("Search Wikipedia and return a summary of the top result")]
    public async Task<string> SearchWikipediaAsync(
        [Description("Search term")] string query,
        [Description("Number of sentences in summary")] int sentences = 5,
        [Description("Language code, e.g. en, id, fr, de")] string lang = "en")
    {
        try
        {
            var searchUrl = $"https://{lang}.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(query.Replace(' ', '_'))}";
            var res = await _http.GetStringAsync(searchUrl);
            var doc = System.Text.Json.JsonDocument.Parse(res);

            var title   = doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() : query;
            var extract = doc.RootElement.TryGetProperty("extract", out var e) ? e.GetString() : "";
            var url     = doc.RootElement.TryGetProperty("content_urls", out var cu)
                ? cu.GetProperty("desktop").GetProperty("page").GetString() : "";

            var parts = extract?.Split(". ").Take(sentences).ToList() ?? new List<string>();
            return $"📖 **{title}**\n{string.Join(". ", parts)}.\n\n🔗 {url}";
        }
        catch { return $"No Wikipedia article found for '{query}'."; }
    }

    [KernelFunction, Description("Search for news articles on a topic")]
    public async Task<string> SearchNewsAsync(
        [Description("Search topic")] string query,
        [Description("Number of results")] int numResults = 5)
    {
        // Uses Google News RSS (no API key needed)
        try
        {
            var url = $"https://news.google.com/rss/search?q={Uri.EscapeDataString(query)}&hl=en-US&gl=US&ceid=US:en";
            var xml = await _http.GetStringAsync(url);
            var doc = new System.Xml.XmlDocument();
            doc.LoadXml(xml);
            var items = doc.SelectNodes("//item");
            if (items == null || items.Count == 0) return "No news found.";

            var sb = new StringBuilder($"📰 News for '{query}':\n\n");
            int shown = 0;
            foreach (System.Xml.XmlNode item in items)
            {
                if (shown >= numResults) break;
                var title = item["title"]?.InnerText;
                var link  = item["link"]?.InnerText;
                var pub   = item["pubDate"]?.InnerText;
                var source = item["source"]?.InnerText;
                if (!string.IsNullOrEmpty(title))
                {
                    sb.AppendLine($"{shown+1}. **{title}**");
                    if (!string.IsNullOrEmpty(source)) sb.AppendLine($"   Source: {source}");
                    if (!string.IsNullOrEmpty(pub)) sb.AppendLine($"   Date: {pub}");
                    if (!string.IsNullOrEmpty(link)) sb.AppendLine($"   URL: {link}");
                    sb.AppendLine();
                    shown++;
                }
            }
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex) { return $"Error fetching news: {ex.Message}"; }
    }

    [KernelFunction, Description("Search for images on the web (returns URLs only)")]
    public async Task<string> SearchImagesAsync(
        [Description("Search query")] string query,
        [Description("Number of results")] int numResults = 5)
    {
        if (string.IsNullOrEmpty(_config.Plugins.Search.BingApiKey))
            return "Image search requires Bing API key (Plugins:Search:BingApiKey in app.config).";

        var url = $"https://api.bing.microsoft.com/v7.0/images/search?q={Uri.EscapeDataString(query)}&count={numResults}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("Ocp-Apim-Subscription-Key", _config.Plugins.Search.BingApiKey);
        var res = await _http.SendAsync(req);
        var json = await res.Content.ReadAsStringAsync();
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var values = doc.RootElement.GetProperty("value");
        var sb = new StringBuilder($"🖼️ Images for '{query}':\n\n");
        int i = 1;
        foreach (var img in values.EnumerateArray().Take(numResults))
        {
            sb.AppendLine($"{i}. {img.GetProperty("contentUrl").GetString()}");
            sb.AppendLine($"   Name: {img.GetProperty("name").GetString()}");
            i++;
        }
        return sb.ToString().TrimEnd();
    }

    // ── Private ────────────────────────────────────────────────
    private async Task<string> SearchBingAsync(string query, int n)
    {
        if (string.IsNullOrEmpty(_config.Plugins.Search.BingApiKey))
            return "Bing API key not configured.";
        var url = $"https://api.bing.microsoft.com/v7.0/search?q={Uri.EscapeDataString(query)}&count={n}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("Ocp-Apim-Subscription-Key", _config.Plugins.Search.BingApiKey);
        var res = await _http.SendAsync(req);
        var json = await res.Content.ReadAsStringAsync();
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var results = doc.RootElement.GetProperty("webPages").GetProperty("value");
        var sb = new StringBuilder();
        foreach (var r in results.EnumerateArray().Take(n))
        {
            sb.AppendLine($"• **{r.GetProperty("name").GetString()}**");
            sb.AppendLine($"  {r.GetProperty("url").GetString()}");
            sb.AppendLine($"  {r.GetProperty("snippet").GetString()}");
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private async Task<string> SearchGoogleAsync(string query, int n)
    {
        if (string.IsNullOrEmpty(_config.Plugins.Search.GoogleApiKey))
            return "Google API key not configured.";
        var url = $"https://www.googleapis.com/customsearch/v1?key={_config.Plugins.Search.GoogleApiKey}" +
                  $"&cx={_config.Plugins.Search.GoogleCseId}&q={Uri.EscapeDataString(query)}&num={n}";
        var json = await _http.GetStringAsync(url);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var sb = new StringBuilder();
        foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray().Take(n))
        {
            sb.AppendLine($"• **{item.GetProperty("title").GetString()}**");
            sb.AppendLine($"  {item.GetProperty("link").GetString()}");
            sb.AppendLine($"  {item.GetProperty("snippet").GetString()}");
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private async Task<string> SearchDuckDuckGoAsync(string query)
    {
        var url = $"https://api.duckduckgo.com/?q={Uri.EscapeDataString(query)}&format=json&no_html=1";
        var json = await _http.GetStringAsync(url);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var sb = new StringBuilder();
        if (doc.RootElement.TryGetProperty("Abstract", out var abs) && abs.GetString()?.Length > 0)
            sb.AppendLine($"📌 {abs.GetString()}\n");
        if (doc.RootElement.TryGetProperty("RelatedTopics", out var topics))
            foreach (var t in topics.EnumerateArray().Take(5))
                if (t.TryGetProperty("Text", out var txt)) sb.AppendLine($"• {txt.GetString()}");
        return sb.Length > 0 ? sb.ToString().TrimEnd() : "No results found.";
    }
}
