using System.ComponentModel;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.SemanticKernel;

namespace SKClaw.Core.Skills;

/// <summary>
/// HttpSkill — Full HTTP client, web scraping helpers, REST API testing,
/// webhooks, URL utilities, and basic HTML parsing.
/// </summary>
public class HttpSkill
{
    private static readonly HttpClientHandler _handler = new()
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = DecompressionMethods.All,
        MaxAutomaticRedirections = 10
    };
    public static readonly HttpClient _http = new(_handler) { Timeout = TimeSpan.FromSeconds(60) };

    // ── Basic HTTP verbs ───────────────────────────────────────

    [KernelFunction, Description("Make an HTTP GET request and return response body + status")]
    public async Task<string> GetAsync(
        [Description("URL to fetch")] string url,
        [Description("Optional request headers as JSON object, e.g. {\"Authorization\":\"Bearer token\"}")] string headers = "",
        [Description("Return only body (true) or include status/headers (false)")] bool bodyOnly = false)
    {
        using var req = BuildRequest(HttpMethod.Get, url, null, headers);
        return await SendAsync(req, bodyOnly);
    }

    [KernelFunction, Description("Make an HTTP POST request with JSON body")]
    public async Task<string> PostJsonAsync(
        [Description("URL")] string url,
        [Description("JSON body")] string jsonBody,
        [Description("Additional headers as JSON object")] string headers = "")
    {
        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        using var req = BuildRequest(HttpMethod.Post, url, content, headers);
        return await SendAsync(req, false);
    }

    [KernelFunction, Description("Make an HTTP POST request with form data")]
    public async Task<string> PostFormAsync(
        [Description("URL")] string url,
        [Description("Form data as JSON object, e.g. {\"key\":\"value\"}")] string formData,
        [Description("Additional headers as JSON object")] string headers = "")
    {
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(formData) ?? new Dictionary<string, string>();
        var content = new FormUrlEncodedContent(dict);
        using var req = BuildRequest(HttpMethod.Post, url, content, headers);
        return await SendAsync(req, false);
    }

    [KernelFunction, Description("Make an HTTP PUT request with JSON body")]
    public async Task<string> PutAsync(
        [Description("URL")] string url,
        [Description("JSON body")] string jsonBody,
        [Description("Additional headers as JSON object")] string headers = "")
    {
        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        using var req = BuildRequest(HttpMethod.Put, url, content, headers);
        return await SendAsync(req, false);
    }

    [KernelFunction, Description("Make an HTTP PATCH request with JSON body")]
    public async Task<string> PatchAsync(
        [Description("URL")] string url,
        [Description("JSON body")] string jsonBody,
        [Description("Additional headers as JSON object")] string headers = "")
    {
        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        using var req = BuildRequest(HttpMethod.Patch, url, content, headers);
        return await SendAsync(req, false);
    }

    [KernelFunction, Description("Make an HTTP DELETE request")]
    public async Task<string> DeleteAsync(
        [Description("URL")] string url,
        [Description("Additional headers as JSON object")] string headers = "")
    {
        using var req = BuildRequest(HttpMethod.Delete, url, null, headers);
        return await SendAsync(req, false);
    }

    [KernelFunction, Description("Make an HTTP HEAD request and return only the response headers")]
    public async Task<string> HeadAsync([Description("URL")] string url)
    {
        using var req = new HttpRequestMessage(HttpMethod.Head, url);
        var res = await _http.SendAsync(req);
        var sb = new StringBuilder();
        sb.AppendLine($"Status: {(int)res.StatusCode} {res.ReasonPhrase}");
        foreach (var h in res.Headers) sb.AppendLine($"{h.Key}: {string.Join(", ", h.Value)}");
        foreach (var h in res.Content.Headers) sb.AppendLine($"{h.Key}: {string.Join(", ", h.Value)}");
        return sb.ToString();
    }

    // ── Advanced Requests ──────────────────────────────────────

    [KernelFunction, Description("Download a file from a URL and save to workspace")]
    public async Task<string> DownloadFileAsync(
        [Description("URL to download")] string url,
        [Description("Local filename to save as")] string filename,
        [Description("Workspace directory path")] string workspaceDir = "")
    {
        var dir = string.IsNullOrEmpty(workspaceDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".skclaw", "workspace")
            : workspaceDir;
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, filename);

        var data = await _http.GetByteArrayAsync(url);
        await File.WriteAllBytesAsync(dest, data);
        return $"Downloaded {FormatSize(data.Length)} to '{filename}'";
    }

    [KernelFunction, Description("Fetch and extract text content from a web page (strips HTML tags)")]
    public async Task<string> FetchWebPageTextAsync(
        [Description("URL of the web page")] string url,
        [Description("Max characters to return")] int maxChars = 8000)
    {
        var html = await _http.GetStringAsync(url);
        // Strip script/style blocks
        html = System.Text.RegularExpressions.Regex.Replace(html,
            @"<(script|style)[^>]*>.*?</(script|style)>", "", System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        // Strip HTML tags
        var text = System.Text.RegularExpressions.Regex.Replace(html, @"<[^>]+>", " ");
        // Decode HTML entities
        text = System.Web.HttpUtility.HtmlDecode(text);
        // Collapse whitespace
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length > maxChars ? text[..maxChars] + $"...[truncated]" : text;
    }

    [KernelFunction, Description("Extract all links (href) from a web page")]
    public async Task<string> ExtractLinksAsync(
        [Description("URL of the web page")] string url,
        [Description("Filter: all, internal, external, images, scripts")] string filter = "all")
    {
        var html = await _http.GetStringAsync(url);
        var baseUri = new Uri(url);
        var baseOrigin = baseUri.GetLeftPart(UriPartial.Authority);
        var matches = System.Text.RegularExpressions.Regex.Matches(html, @"href=""([^""]+)""", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var links = matches.Select(m => m.Groups[1].Value).Distinct().ToList();

        var filtered = filter.ToLower() switch
        {
            "internal" => links.Where(l => !l.StartsWith("http") || l.StartsWith(baseOrigin)).ToList(),
            "external" => links.Where(l => l.StartsWith("http") && !l.StartsWith(baseOrigin)).ToList(),
            _ => links
        };

        return $"Found {filtered.Count} links:\n" + string.Join("\n", filtered.Take(50));
    }

    [KernelFunction, Description("Get HTTP response headers from a URL")]
    public async Task<string> GetHeadersAsync([Description("URL")] string url)
    {
        var res = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        var sb = new StringBuilder($"Status: {(int)res.StatusCode} {res.ReasonPhrase}\n");
        foreach (var h in res.Headers.Concat(res.Content.Headers))
            sb.AppendLine($"{h.Key}: {string.Join(", ", h.Value)}");
        return sb.ToString();
    }

    // ── URL Utilities ──────────────────────────────────────────

    [KernelFunction, Description("Parse a URL and return its components: scheme, host, path, query params, fragment")]
    public string ParseUrl([Description("URL to parse")] string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "Invalid URL";
        var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var sb = new StringBuilder();
        sb.AppendLine($"Scheme   : {uri.Scheme}");
        sb.AppendLine($"Host     : {uri.Host}");
        sb.AppendLine($"Port     : {(uri.IsDefaultPort ? "(default)" : uri.Port.ToString())}");
        sb.AppendLine($"Path     : {uri.AbsolutePath}");
        sb.AppendLine($"Query    : {uri.Query}");
        sb.AppendLine($"Fragment : {uri.Fragment}");
        if (queryParams.Count > 0)
        {
            sb.AppendLine("Query params:");
            foreach (string key in queryParams)
                sb.AppendLine($"  {key} = {queryParams[key]}");
        }
        return sb.ToString();
    }

    [KernelFunction, Description("Build a URL from base URL and query parameters")]
    public string BuildUrl(
        [Description("Base URL")] string baseUrl,
        [Description("Query parameters as JSON object")] string queryParams)
    {
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(queryParams) ?? [];
        var query = string.Join("&", dict.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return string.IsNullOrEmpty(query) ? baseUrl : $"{baseUrl.TrimEnd('?')}{(baseUrl.Contains('?') ? "&" : "?")}{query}";
    }

    [KernelFunction, Description("Encode or decode a URL string")]
    public string EncodeUrl(
        [Description("URL or string")] string input,
        [Description("Direction: encode or decode")] string direction = "encode",
        [Description("Mode: component (default) or full")] string mode = "component")
    {
        if (direction.ToLower() == "decode")
            return mode == "full" ? Uri.UnescapeDataString(input) : Uri.UnescapeDataString(input);
        return mode == "full"
            ? Uri.EscapeUriString(input)
            : Uri.EscapeDataString(input);
    }

    // ── GraphQL & REST Testing ─────────────────────────────────

    [KernelFunction, Description("Execute a GraphQL query")]
    public async Task<string> GraphQLQueryAsync(
        [Description("GraphQL endpoint URL")] string endpoint,
        [Description("GraphQL query string")] string query,
        [Description("Variables as JSON object (optional)")] string variables = "{}",
        [Description("Authorization header value (optional)")] string authorization = "")
    {
        var body = JsonSerializer.Serialize(new
        {
            query,
            variables = JsonSerializer.Deserialize<JsonElement>(variables)
        });
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrEmpty(authorization))
            req.Headers.TryAddWithoutValidation("Authorization", authorization);
        var res = await _http.SendAsync(req);
        var content = await res.Content.ReadAsStringAsync();
        return FormatJson(content);
    }

    [KernelFunction, Description("Pretty-print and validate a JSON string")]
    public string FormatJsonString([Description("JSON string to format")] string json)
    {
        return FormatJson(json);
    }

    [KernelFunction, Description("Check DNS records for a domain (A, MX, TXT, CNAME)")]
    public async Task<string> CheckDnsAsync([Description("Domain name")] string domain)
    {
        try
        {
            var entry = await Dns.GetHostEntryAsync(domain);
            var ips = string.Join(", ", entry.Aliases.Concat(entry.AddressList.Select(a => a.ToString())));
            return $"DNS for {domain}:\nHost: {entry.HostName}\nAddresses: {ips}";
        }
        catch (Exception ex) { return $"DNS lookup failed for {domain}: {ex.Message}"; }
    }

    [KernelFunction, Description("Ping a host and check if it's reachable")]
    public async Task<string> PingAsync(
        [Description("Host or IP to ping")] string host,
        [Description("Timeout in milliseconds")] int timeoutMs = 3000)
    {
        try
        {
            using var ping = new System.Net.NetworkInformation.Ping();
            var reply = await ping.SendPingAsync(host, timeoutMs);
            return reply.Status == System.Net.NetworkInformation.IPStatus.Success
                ? $"Ping {host}: Success | Time={reply.RoundtripTime}ms | TTL={reply.Options?.Ttl}"
                : $"Ping {host}: {reply.Status}";
        }
        catch (Exception ex) { return $"Ping failed: {ex.Message}"; }
    }

    // ── Webhook / Notification ─────────────────────────────────

    [KernelFunction, Description("Send a message to a Slack webhook URL")]
    public async Task<string> SendSlackWebhookAsync(
        [Description("Slack Incoming Webhook URL")] string webhookUrl,
        [Description("Message text")] string message,
        [Description("Channel override (optional, e.g. #general)")] string channel = "",
        [Description("Username override (optional)")] string username = "SKClaw")
    {
        var payload = new Dictionary<string, object> { ["text"] = message, ["username"] = username };
        if (!string.IsNullOrEmpty(channel)) payload["channel"] = channel;
        var json = JsonSerializer.Serialize(payload);
        var res = await _http.PostAsync(webhookUrl, new StringContent(json, Encoding.UTF8, "application/json"));
        return res.IsSuccessStatusCode ? "Slack message sent." : $"Failed: {(int)res.StatusCode}";
    }

    [KernelFunction, Description("Send a Discord message via webhook")]
    public async Task<string> SendDiscordWebhookAsync(
        [Description("Discord Webhook URL")] string webhookUrl,
        [Description("Message content")] string message,
        [Description("Username override")] string username = "SKClaw")
    {
        var payload = new { content = message, username };
        var res = await _http.PostAsJsonAsync(webhookUrl, payload);
        return res.IsSuccessStatusCode ? "Discord message sent." : $"Failed: {(int)res.StatusCode}";
    }

    [KernelFunction, Description("Send a generic JSON webhook POST")]
    public async Task<string> SendWebhookAsync(
        [Description("Webhook URL")] string url,
        [Description("JSON payload")] string jsonPayload,
        [Description("Secret for HMAC-SHA256 signature header (optional)")] string secret = "",
        [Description("Signature header name")] string signatureHeader = "X-Signature")
    {
        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        if (!string.IsNullOrEmpty(secret))
        {
            var sig = ComputeHmac(jsonPayload, secret);
            req.Headers.TryAddWithoutValidation(signatureHeader, $"sha256={sig}");
        }
        var res = await _http.SendAsync(req);
        return $"Status: {(int)res.StatusCode} {res.ReasonPhrase}\n{await res.Content.ReadAsStringAsync()}";
    }

    // ── Private Helpers ────────────────────────────────────────
    private static HttpRequestMessage BuildRequest(HttpMethod method, string url,
        HttpContent? content, string headersJson)
    {
        var req = new HttpRequestMessage(method, url) { Content = content };
        req.Headers.UserAgent.ParseAdd("SKClaw/1.0");
        if (!string.IsNullOrEmpty(headersJson))
        {
            var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson) ?? new Dictionary<string, string>();
            foreach (var kv in headers)
                req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
        }
        return req;
    }

    private static async Task<string> SendAsync(HttpRequestMessage req, bool bodyOnly)
    {
        try
        {
            var res = await _http.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();
            if (bodyOnly) return body.Length > 8000 ? body[..8000] + "[truncated]" : body;

            var sb = new StringBuilder();
            sb.AppendLine($"Status : {(int)res.StatusCode} {res.ReasonPhrase}");
            sb.AppendLine($"Length : {body.Length} chars");
            if (res.Content.Headers.ContentType != null)
                sb.AppendLine($"Content-Type: {res.Content.Headers.ContentType}");
            sb.AppendLine();
            var bodyOut = body.Length > 8000 ? body[..8000] + "\n...[truncated]" : body;
            // Try to pretty-print JSON
            if (body.TrimStart().StartsWith('{') || body.TrimStart().StartsWith('['))
                bodyOut = FormatJson(body);
            sb.Append(bodyOut);
            return sb.ToString();
        }
        catch (HttpRequestException ex) { return $"HTTP Error: {ex.Message}"; }
        catch (TaskCanceledException) { return "Request timed out."; }
    }

    private static string FormatJson(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch { return json; }
    }

    private static string ComputeHmac(string data, string secret)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        using var hmac = new System.Security.Cryptography.HMACSHA256(key);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLower();
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1048576 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / 1048576.0:F1} MB"
    };
}
