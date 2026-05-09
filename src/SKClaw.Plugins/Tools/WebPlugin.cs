using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.SemanticKernel;

namespace SKClaw.Plugins.Tools;

/// <summary>
/// WebPlugin — RSS/Atom feed reader, web scraping, sitemap parsing,
/// social media content helpers, and web monitoring.
/// All free/no-API-key operations where possible.
/// </summary>
public class WebPlugin
{
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders = { { "User-Agent", "Mozilla/5.0 (compatible; SKClaw/1.0)" } }
    };
    private readonly Kernel _kernel;
    private readonly string _dataDir;
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public WebPlugin(Kernel kernel, string dataDir = "")
    {
        _kernel  = kernel;
        _dataDir = string.IsNullOrEmpty(dataDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".skclaw", "web")
            : dataDir;
        Directory.CreateDirectory(_dataDir);
    }

    // ── RSS / Atom Feed ────────────────────────────────────────

    [KernelFunction, Description("Fetch and parse an RSS or Atom feed, returning recent articles")]
    public async Task<string> ReadRssFeedAsync(
        [Description("RSS/Atom feed URL")] string feedUrl,
        [Description("Number of items to return (1-30)")] int count = 10,
        [Description("Show full description or just title+link")] bool fullDescription = false)
    {
        try
        {
            var xml  = await _http.GetStringAsync(feedUrl);
            var doc  = new XmlDocument();
            doc.LoadXml(xml);

            // Detect format: RSS vs Atom
            bool isAtom = doc.DocumentElement?.NamespaceURI?.Contains("atom") == true ||
                          doc.DocumentElement?.LocalName == "feed";

            var sb   = new StringBuilder();
            string feedTitle;
            XmlNodeList? items;

            if (isAtom)
            {
                var ns = new XmlNamespaceManager(doc.NameTable);
                ns.AddNamespace("a", "http://www.w3.org/2005/Atom");
                feedTitle = doc.SelectSingleNode("//a:feed/a:title", ns)?.InnerText ?? feedUrl;
                items = doc.SelectNodes("//a:entry", ns);
                sb.AppendLine($"📰 {feedTitle}\n");
                if (items == null) return $"No entries found in Atom feed: {feedUrl}";
                int shown = 0;
                foreach (XmlNode item in items)
                {
                    if (shown >= count) break;
                    var title   = item.SelectSingleNode("a:title", ns)?.InnerText?.Trim();
                    var link    = item.SelectSingleNode("a:link/@href", ns)?.Value
                               ?? item.SelectSingleNode("a:link", ns)?.InnerText;
                    var pubDate = item.SelectSingleNode("a:published", ns)?.InnerText
                               ?? item.SelectSingleNode("a:updated", ns)?.InnerText;
                    var summary = item.SelectSingleNode("a:summary", ns)?.InnerText?.Trim()
                               ?? item.SelectSingleNode("a:content", ns)?.InnerText?.Trim();

                    sb.AppendLine($"{shown + 1}. **{title}**");
                    if (!string.IsNullOrEmpty(pubDate)) sb.AppendLine($"   📅 {ParsePubDate(pubDate)}");
                    if (!string.IsNullOrEmpty(link))    sb.AppendLine($"   🔗 {link}");
                    if (fullDescription && !string.IsNullOrEmpty(summary))
                        sb.AppendLine($"   {StripHtml(summary)[..Math.Min(300, StripHtml(summary).Length)]}...");
                    sb.AppendLine();
                    shown++;
                }
            }
            else
            {
                feedTitle = doc.SelectSingleNode("//channel/title")?.InnerText ?? feedUrl;
                items = doc.SelectNodes("//item");
                sb.AppendLine($"📰 {feedTitle}\n");
                if (items == null) return "No items found in RSS feed.";
                int shown = 0;
                foreach (XmlNode item in items)
                {
                    if (shown >= count) break;
                    var title   = item["title"]?.InnerText?.Trim();
                    var link    = item["link"]?.InnerText?.Trim();
                    var pubDate = item["pubDate"]?.InnerText;
                    var desc    = item["description"]?.InnerText?.Trim();

                    sb.AppendLine($"{shown + 1}. **{title}**");
                    if (!string.IsNullOrEmpty(pubDate)) sb.AppendLine($"   📅 {ParsePubDate(pubDate)}");
                    if (!string.IsNullOrEmpty(link))    sb.AppendLine($"   🔗 {link}");
                    if (fullDescription && !string.IsNullOrEmpty(desc))
                        sb.AppendLine($"   {StripHtml(desc)[..Math.Min(300, StripHtml(desc).Length)]}...");
                    sb.AppendLine();
                    shown++;
                }
            }
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex) { return $"Feed error: {ex.Message}"; }
    }

    [KernelFunction, Description("Subscribe to an RSS feed and save it locally for monitoring")]
    public async Task<string> SubscribeToFeedAsync(
        [Description("Feed URL")] string feedUrl,
        [Description("A friendly name for this feed")] string name)
    {
        var subs = await LoadListAsync<FeedSubscription>("feed_subscriptions.json");
        if (subs.Any(s => s.Url == feedUrl))
            return $"Already subscribed to: {feedUrl}";

        subs.Add(new FeedSubscription { Name = name, Url = feedUrl, AddedAt = DateTimeOffset.UtcNow });
        await SaveListAsync("feed_subscriptions.json", subs);
        return $"✅ Subscribed to feed: {name} ({feedUrl})";
    }

    [KernelFunction, Description("List all subscribed RSS feeds and fetch their latest headlines")]
    public async Task<string> GetFeedUpdatesAsync(
        [Description("Items per feed to show")] int itemsPerFeed = 3)
    {
        var subs = await LoadListAsync<FeedSubscription>("feed_subscriptions.json");
        if (subs.Count == 0) return "No feed subscriptions. Use SubscribeToFeed first.";

        var sb = new StringBuilder($"📡 Feed Updates ({subs.Count} feeds)\n\n");
        foreach (var sub in subs)
        {
            sb.AppendLine($"── {sub.Name} ──");
            try
            {
                var content = await ReadRssFeedAsync(sub.Url, itemsPerFeed);
                // Skip the feed title line and return the articles
                var lines = content.Split('\n').Skip(2).Take(itemsPerFeed * 5);
                sb.AppendLine(string.Join("\n", lines));
            }
            catch { sb.AppendLine($"  ⚠️ Could not fetch feed."); }
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    // ── Web Scraping ───────────────────────────────────────────

    [KernelFunction, Description("Scrape a web page and extract structured content: title, headings, paragraphs, links, meta tags")]
    public async Task<string> ScrapePageAsync(
        [Description("URL to scrape")] string url,
        [Description("Extract: all, text, links, headings, meta, images, tables")] string extract = "text",
        [Description("Max content length")] int maxLength = 5000)
    {
        try
        {
            var html = await _http.GetStringAsync(url);
            return extract.ToLower() switch
            {
                "text"     => ExtractText(html, maxLength),
                "links"    => ExtractLinks(html, url),
                "headings" => ExtractHeadings(html),
                "meta"     => ExtractMeta(html),
                "images"   => ExtractImages(html, url),
                "tables"   => ExtractTables(html),
                _          => $"=== TEXT ===\n{ExtractText(html, 2000)}\n\n=== LINKS ===\n{ExtractLinks(html, url)}\n\n=== META ===\n{ExtractMeta(html)}"
            };
        }
        catch (Exception ex) { return $"Scrape error: {ex.Message}"; }
    }

    [KernelFunction, Description("Scrape multiple pages and aggregate results")]
    public async Task<string> ScrapeMultiplePagesAsync(
        [Description("Comma-separated list of URLs (max 5)")] string urls,
        [Description("What to extract from each page: text, links, headings")] string extract = "text",
        [Description("Max chars per page")] int maxPerPage = 2000)
    {
        var urlList = urls.Split(',').Select(u => u.Trim()).Take(5).ToList();
        var sb = new StringBuilder();
        foreach (var url in urlList)
        {
            sb.AppendLine($"\n=== {url} ===");
            sb.AppendLine(await ScrapePageAsync(url, extract, maxPerPage));
        }
        return sb.ToString().TrimEnd();
    }

    [KernelFunction, Description("Monitor a web page for changes (stores a snapshot and compares)")]
    public async Task<string> MonitorPageAsync(
        [Description("URL to monitor")] string url,
        [Description("Selector hint or 'body' for full page text")] string selector = "body")
    {
        var key = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(url)).Replace('/', '_').Replace('+', '-')[..20];
        var snapshotPath = Path.Combine(_dataDir, $"snapshot_{key}.txt");

        var html    = await _http.GetStringAsync(url);
        var current = ExtractText(html, 20000);

        if (!File.Exists(snapshotPath))
        {
            await File.WriteAllTextAsync(snapshotPath, current);
            return $"📸 Baseline snapshot saved for: {url}\nContent length: {current.Length} chars";
        }

        var previous = await File.ReadAllTextAsync(snapshotPath);
        if (previous == current)
            return $"✅ No changes detected on: {url}";

        // Simple diff summary
        var prev = previous.Split('\n');
        var curr = current.Split('\n');
        int added   = curr.Except(prev).Count();
        int removed = prev.Except(curr).Count();

        await File.WriteAllTextAsync(snapshotPath, current);
        return $"⚠️ Page changed: {url}\nAdded lines: {added}  |  Removed lines: {removed}\nSnapshot updated.";
    }

    [KernelFunction, Description("Parse a website's sitemap.xml and return all URLs")]
    public async Task<string> ParseSitemapAsync(
        [Description("Sitemap URL (usually https://example.com/sitemap.xml)")] string sitemapUrl,
        [Description("Filter URLs containing this string")] string filter = "",
        [Description("Max URLs to return")] int maxUrls = 50)
    {
        try
        {
            var xml = await _http.GetStringAsync(sitemapUrl);
            var doc = new XmlDocument();
            doc.LoadXml(xml);

            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("sm", "http://www.sitemaps.org/schemas/sitemap/0.9");

            // Check for sitemap index
            var sitemapRefs = doc.SelectNodes("//sm:sitemap/sm:loc", ns);
            if (sitemapRefs?.Count > 0)
            {
                var sb = new StringBuilder($"Sitemap index with {sitemapRefs.Count} sitemaps:\n");
                foreach (XmlNode s in sitemapRefs)
                    sb.AppendLine($"  {s.InnerText}");
                return sb.ToString();
            }

            var urls = doc.SelectNodes("//sm:url/sm:loc", ns);
            if (urls == null) return "No URLs found in sitemap.";

            var all = urls.Cast<XmlNode>()
                .Select(n => n.InnerText)
                .Where(u => string.IsNullOrEmpty(filter) || u.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .Take(maxUrls)
                .ToList();

            return $"Sitemap: {all.Count} URLs{(string.IsNullOrEmpty(filter) ? "" : $" (filtered by '{filter}')")}:\n" +
                   string.Join("\n", all.Select((u, i) => $"  {i + 1}. {u}"));
        }
        catch (Exception ex) { return $"Sitemap error: {ex.Message}"; }
    }

    [KernelFunction, Description("Check if a URL returns a successful HTTP response and measure response time")]
    public async Task<string> CheckUrlAsync(
        [Description("URL to check")] string url,
        [Description("Follow redirects?")] bool followRedirects = true)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = followRedirects };
            using var client  = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            var res = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            sw.Stop();

            var redirect = !followRedirects && res.Headers.Location != null
                ? $"\nRedirects to: {res.Headers.Location}" : "";

            return $"""
                URL     : {url}
                Status  : {(int)res.StatusCode} {res.ReasonPhrase}
                Time    : {sw.ElapsedMilliseconds} ms
                Server  : {res.Headers.Server}
                Content : {res.Content.Headers.ContentType}{redirect}
                """;
        }
        catch (Exception ex)
        {
            sw.Stop();
            return $"❌ {url} — Error: {ex.Message} ({sw.ElapsedMilliseconds} ms)";
        }
    }

    [KernelFunction, Description("Batch check multiple URLs for availability")]
    public async Task<string> BatchCheckUrlsAsync(
        [Description("Comma-separated list of URLs to check")] string urls,
        [Description("Timeout per request in seconds")] int timeoutSeconds = 10)
    {
        var urlList = urls.Split(',').Select(u => u.Trim()).Where(u => u.Length > 0).Take(20).ToList();
        var sb = new StringBuilder($"URL Availability Check ({urlList.Count} URLs):\n\n");

        var tasks = urlList.Select(async url =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
                var res = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                sw.Stop();
                return (url, ok: res.IsSuccessStatusCode, status: (int)res.StatusCode, ms: sw.ElapsedMilliseconds);
            }
            catch
            {
                sw.Stop();
                return (url, ok: false, status: 0, ms: sw.ElapsedMilliseconds);
            }
        });

        var results = await Task.WhenAll(tasks);
        foreach (var (url, ok, status, ms) in results)
            sb.AppendLine($"  {(ok ? "✅" : "❌")} [{(status == 0 ? "ERR" : status.ToString())}] {ms,5}ms  {url}");

        int okCount   = results.Count(r => r.ok);
        int failCount = results.Length - okCount;
        sb.AppendLine($"\nSummary: {okCount} OK, {failCount} failed");
        return sb.ToString().TrimEnd();
    }

    [KernelFunction, Description("Fetch and summarise a web page using AI")]
    public async Task<string> SummariseWebPageAsync(
        [Description("URL of the page to summarise")] string url,
        [Description("What to focus on in the summary")] string focus = "main content")
    {
        var text = await ScrapePageAsync(url, "text", 6000);
        if (text.StartsWith("Scrape error")) return text;

        var prompt = $"Summarise the following web page content, focusing on: {focus}\n\nURL: {url}\n\nContent:\n{text}";
        var result = await _kernel.InvokePromptAsync(prompt);
        return result.GetValue<string>() ?? "";
    }

    [KernelFunction, Description("Get WHOIS-like information for a domain (registration, expiry, nameservers)")]
    public async Task<string> GetDomainInfoAsync([Description("Domain name, e.g. example.com")] string domain)
    {
        try
        {
            // Using whois.domaintools.com free API
            var url = $"https://api.domainsdb.info/v1/domains/search?domain={domain.Split('.')[0]}&zone={string.Join('.', domain.Split('.').Skip(1))}&limit=1";
            var json = await _http.GetStringAsync(url);
            var doc  = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("domains", out var domains) || domains.GetArrayLength() == 0)
                return $"No domain info found for {domain}.";

            var d = domains[0];
            var sb = new StringBuilder($"🌐 Domain: {domain}\n");
            if (d.TryGetProperty("create_date", out var cd)) sb.AppendLine($"Created : {cd.GetString()}");
            if (d.TryGetProperty("update_date", out var ud)) sb.AppendLine($"Updated : {ud.GetString()}");
            if (d.TryGetProperty("country",     out var c))  sb.AppendLine($"Country : {c.GetString()}");
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex) { return $"Domain lookup failed: {ex.Message}"; }
    }

    // ── Social Media Content Helpers ───────────────────────────

    [KernelFunction, Description("Generate a content calendar with post ideas for multiple platforms")]
    public async Task<string> GenerateContentCalendarAsync(
        [Description("Topic or niche, e.g. 'digital marketing', 'cooking', 'software development'")] string niche,
        [Description("Number of days to plan")] int days = 7,
        [Description("Platforms: twitter,linkedin,instagram (comma-separated)")] string platforms = "twitter,linkedin,instagram")
    {
        var platList = platforms.Split(',').Select(p => p.Trim()).ToList();
        var prompt   = $"""
            Create a {days}-day social media content calendar for the niche: {niche}
            Platforms: {string.Join(", ", platList)}
            
            For each day, provide:
            - Day number and theme
            - One post idea per platform with a brief copy
            - Suggested hashtags
            - Best posting time
            
            Make content varied: mix educational, entertaining, promotional, and engagement posts.
            """;
        var result = await _kernel.InvokePromptAsync(prompt);
        return result.GetValue<string>() ?? "";
    }

    [KernelFunction, Description("Analyse text for social media SEO: keyword density, readability, hashtag suggestions")]
    public async Task<string> AnalyseSocialContentAsync(
        [Description("Social media post text to analyse")] string text,
        [Description("Target platform: twitter, linkedin, instagram, tiktok")] string platform = "linkedin")
    {
        var charLimit = platform.ToLower() switch
        {
            "twitter" or "x" => 280,
            "instagram" => 2200,
            "tiktok"    => 2200,
            _           => 3000
        };

        int charCount   = text.Length;
        var words       = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hashtags    = words.Where(w => w.StartsWith('#')).ToList();
        var mentions    = words.Where(w => w.StartsWith('@')).ToList();
        var urls        = Regex.Matches(text, @"https?://\S+").Select(m => m.Value).ToList();
        bool withinLimit = charCount <= charLimit;

        var aiAnalysis = await _kernel.InvokePromptAsync(
            $"Rate this {platform} post on: engagement potential (1-10), clarity (1-10), CTA strength (1-10). Suggest 3 improvements.\n\nPost: {text}");

        return $"""
            📊 Social Content Analysis ({platform})
            Characters: {charCount}/{charLimit} {(withinLimit ? "✅" : "❌ Over limit!")}
            Words     : {words.Length}
            Hashtags  : {hashtags.Count} ({string.Join(" ", hashtags)})
            Mentions  : {mentions.Count} ({string.Join(" ", mentions)})
            URLs      : {urls.Count}
            
            AI Assessment:
            {aiAnalysis.GetValue<string>()}
            """;
    }

    // ── Private Helpers ────────────────────────────────────────
    private static string ExtractText(string html, int maxLength)
    {
        html = Regex.Replace(html, @"<(script|style|nav|footer|header)[^>]*>.*?</\1>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var text = Regex.Replace(html, @"<[^>]+>", " ");
        text = System.Web.HttpUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length > maxLength ? text[..maxLength] + "...[truncated]" : text;
    }

    private static string ExtractLinks(string html, string baseUrl)
    {
        var matches = Regex.Matches(html, @"<a[^>]+href=""([^""]+)""[^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var links = matches.Select(m =>
        {
            var href = m.Groups[1].Value;
            var text = StripHtml(m.Groups[2].Value).Trim();
            if (!href.StartsWith("http") && !href.StartsWith("//") && !href.StartsWith("#"))
            {
                if (Uri.TryCreate(new Uri(baseUrl), href, out var abs)) href = abs.ToString();
            }
            return $"  [{text}] → {href}";
        }).Distinct().Take(50).ToList();
        return $"Links found: {links.Count}\n" + string.Join("\n", links);
    }

    private static string ExtractHeadings(string html)
    {
        var sb = new StringBuilder("Page Headings:\n");
        for (int level = 1; level <= 6; level++)
        {
            var matches = Regex.Matches(html, $@"<h{level}[^>]*>(.*?)</h{level}>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            foreach (Match m in matches)
                sb.AppendLine($"{"  ".PadRight(level)}H{level}: {StripHtml(m.Groups[1].Value).Trim()}");
        }
        return sb.ToString();
    }

    private static string ExtractMeta(string html)
    {
        var sb = new StringBuilder("Meta Tags:\n");
        var title = Regex.Match(html, @"<title>(.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (title.Success) sb.AppendLine($"  title: {title.Groups[1].Value.Trim()}");

        var metas = Regex.Matches(html, @"<meta\s+(?:name|property)=""([^""]+)""\s+content=""([^""]+)""", RegexOptions.IgnoreCase);
        foreach (Match m in metas) sb.AppendLine($"  {m.Groups[1].Value}: {m.Groups[2].Value.Trim()}");
        return sb.ToString();
    }

    private static string ExtractImages(string html, string baseUrl)
    {
        var matches = Regex.Matches(html, @"<img[^>]+src=""([^""]+)""(?:[^>]+alt=""([^""]*)"")?"  , RegexOptions.IgnoreCase);
        var images  = matches.Select(m => $"  {m.Groups[1].Value}" + (m.Groups[2].Success && m.Groups[2].Value.Length > 0 ? $" (alt: {m.Groups[2].Value})" : ""))
                             .Distinct().Take(30).ToList();
        return $"Images found: {images.Count}\n" + string.Join("\n", images);
    }

    private static string ExtractTables(string html)
    {
        var tables = Regex.Matches(html, @"<table[^>]*>(.*?)</table>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (tables.Count == 0) return "No tables found.";
        var sb = new StringBuilder($"Found {tables.Count} table(s):\n");
        int tableNum = 0;
        foreach (Match table in tables)
        {
            sb.AppendLine($"\nTable {++tableNum}:");
            var rows = Regex.Matches(table.Groups[1].Value, @"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            foreach (Match row in rows.Take(10))
            {
                var cells = Regex.Matches(row.Groups[1].Value, @"<t[dh][^>]*>(.*?)</t[dh]>", RegexOptions.Singleline | RegexOptions.IgnoreCase)
                    .Select(c => StripHtml(c.Groups[1].Value).Trim());
                sb.AppendLine("  | " + string.Join(" | ", cells) + " |");
            }
        }
        return sb.ToString();
    }

    private static string StripHtml(string html) => Regex.Replace(html, @"<[^>]+>", " ").Trim();

    private static string ParsePubDate(string raw)
    {
        if (DateTimeOffset.TryParse(raw, out var dt)) return dt.ToString("yyyy-MM-dd HH:mm");
        return raw;
    }

    private async Task<List<T>> LoadListAsync<T>(string file)
    {
        var path = Path.Combine(_dataDir, file);
        if (!File.Exists(path)) return [];
        return JsonSerializer.Deserialize<List<T>>(await File.ReadAllTextAsync(path), _json) ?? [];
    }

    private async Task SaveListAsync<T>(string file, List<T> data)
        => await File.WriteAllTextAsync(Path.Combine(_dataDir, file), JsonSerializer.Serialize(data, _json));
}

public class FeedSubscription
{
    public string Name    { get; set; } = "";
    public string Url     { get; set; } = "";
    public DateTimeOffset AddedAt { get; set; }
}
