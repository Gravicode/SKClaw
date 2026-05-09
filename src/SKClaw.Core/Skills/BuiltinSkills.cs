using System.ComponentModel;
using System.Net.Http.Json;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.SemanticKernel;
using SKClaw.Core.Configuration;

namespace SKClaw.Core.Skills;
/*
// ─────────────────────────────────────────────────────────────
// TIME SKILL
// ─────────────────────────────────────────────────────────────
public class TimeSkill
{
    [KernelFunction, Description("Get the current date and time")]
    public string GetCurrentTime([Description("Timezone (e.g., UTC, Asia/Jakarta)")] string timezone = "UTC")
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
            return TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).ToString("yyyy-MM-dd HH:mm:ss zzz");
        }
        catch
        {
            return DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");
        }
    }

    [KernelFunction, Description("Calculate the difference between two dates")]
    public string DateDiff(
        [Description("Start date (ISO 8601)")] string startDate,
        [Description("End date (ISO 8601)")] string endDate)
    {
        if (!DateTimeOffset.TryParse(startDate, out var start) ||
            !DateTimeOffset.TryParse(endDate, out var end))
            return "Invalid date format. Use ISO 8601 (e.g., 2024-01-15)";

        var diff = end - start;
        return $"Difference: {(int)diff.TotalDays} days, {diff.Hours} hours, {diff.Minutes} minutes";
    }

    [KernelFunction, Description("Add or subtract time from a date")]
    public string AddTime(
        [Description("Base date (ISO 8601)")] string date,
        [Description("Amount to add (negative to subtract)")] int amount,
        [Description("Unit: days, hours, minutes, months")] string unit)
    {
        if (!DateTimeOffset.TryParse(date, out var dt))
            return "Invalid date format";

        var result = unit.ToLower() switch
        {
            "days" => dt.AddDays(amount),
            "hours" => dt.AddHours(amount),
            "minutes" => dt.AddMinutes(amount),
            "months" => dt.AddMonths(amount),
            _ => dt
        };
        return result.ToString("yyyy-MM-dd HH:mm:ss zzz");
    }
}

// ─────────────────────────────────────────────────────────────
// MATH SKILL
// ─────────────────────────────────────────────────────────────
public class MathSkill
{
    [KernelFunction, Description("Evaluate a mathematical expression")]
    public string Calculate([Description("Math expression, e.g., '2 + 3 * 4' or 'sqrt(16)'")] string expression)
    {
        try
        {
            // Simple safe evaluator for basic math
            var result = EvaluateExpression(expression);
            return result.ToString("G");
        }
        catch (Exception ex)
        {
            return $"Error evaluating '{expression}': {ex.Message}";
        }
    }

    [KernelFunction, Description("Convert units (length, weight, temperature, etc.)")]
    public string ConvertUnit(
        [Description("Value to convert")] double value,
        [Description("Source unit (e.g., km, kg, celsius)")] string fromUnit,
        [Description("Target unit (e.g., miles, lb, fahrenheit)")] string toUnit)
    {
        var key = $"{fromUnit.ToLower()}_to_{toUnit.ToLower()}";
        var result = key switch
        {
            "km_to_miles" => value * 0.621371,
            "miles_to_km" => value * 1.60934,
            "kg_to_lb" => value * 2.20462,
            "lb_to_kg" => value * 0.453592,
            "celsius_to_fahrenheit" => value * 9 / 5 + 32,
            "fahrenheit_to_celsius" => (value - 32) * 5 / 9,
            "m_to_ft" => value * 3.28084,
            "ft_to_m" => value * 0.3048,
            "l_to_gallon" => value * 0.264172,
            "gallon_to_l" => value * 3.78541,
            _ => double.NaN
        };
        return double.IsNaN(result)
            ? $"Cannot convert from {fromUnit} to {toUnit}"
            : $"{value} {fromUnit} = {result:G4} {toUnit}";
    }

    private static double EvaluateExpression(string expr)
    {
        // Basic expression evaluator using DataTable
        var table = new System.Data.DataTable();
        expr = expr.Replace("sqrt(", "SQRT(").Replace("pi", Math.PI.ToString());
        var result = table.Compute(expr, null);
        return Convert.ToDouble(result);
    }
}

// ─────────────────────────────────────────────────────────────
// HTTP SKILL
// ─────────────────────────────────────────────────────────────
public class HttpSkill
{
    protected static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    [KernelFunction, Description("Make an HTTP GET request and return the response")]
    public async Task<string> GetAsync(
        [Description("URL to fetch")] string url,
        [Description("Optional headers as JSON object")] string headers = "")
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(headers))
            {
                var h = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(headers);
                foreach (var kv in h ?? [])
                    req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
            }
            var response = await _http.SendAsync(req);
            var content = await response.Content.ReadAsStringAsync();
            return $"Status: {(int)response.StatusCode}\n{content[..Math.Min(5000, content.Length)]}";
        }
        catch (Exception ex) { return $"HTTP Error: {ex.Message}"; }
    }

    [KernelFunction, Description("Make an HTTP POST request with JSON body")]
    public async Task<string> PostJsonAsync(
        [Description("URL to post to")] string url,
        [Description("JSON body")] string jsonBody,
        [Description("Optional headers as JSON object")] string headers = "")
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrEmpty(headers))
            {
                var h = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(headers);
                foreach (var kv in h ?? [])
                    req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
            }
            var response = await _http.SendAsync(req);
            var content = await response.Content.ReadAsStringAsync();
            return $"Status: {(int)response.StatusCode}\n{content[..Math.Min(5000, content.Length)]}";
        }
        catch (Exception ex) { return $"HTTP Error: {ex.Message}"; }
    }
}

// ─────────────────────────────────────────────────────────────
// FILE SKILL
// ─────────────────────────────────────────────────────────────
public class FileSkill
{
    private static readonly string WorkDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".skclaw", "workspace");

    public FileSkill()
    {
        Directory.CreateDirectory(WorkDir);
    }

    [KernelFunction, Description("Read the contents of a file from the workspace")]
    public async Task<string> ReadFileAsync([Description("Filename in workspace")] string filename)
    {
        try
        {
            var path = GetSafePath(filename);
            if (!File.Exists(path)) return $"File '{filename}' not found.";
            var content = await File.ReadAllTextAsync(path);
            return content.Length > 8000
                ? content[..8000] + $"\n...[truncated, total {content.Length} chars]"
                : content;
        }
        catch (Exception ex) { return $"Error reading file: {ex.Message}"; }
    }

    [KernelFunction, Description("Write content to a file in the workspace")]
    public async Task<string> WriteFileAsync(
        [Description("Filename to create/overwrite")] string filename,
        [Description("Content to write")] string content)
    {
        try
        {
            var path = GetSafePath(filename);
            var dir = Path.GetDirectoryName(path);
            if (dir != null) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(path, content);
            return $"File '{filename}' written successfully ({content.Length} chars).";
        }
        catch (Exception ex) { return $"Error writing file: {ex.Message}"; }
    }

    [KernelFunction, Description("List files in the workspace")]
    public string ListFiles([Description("Optional subdirectory")] string subdirectory = "")
    {
        try
        {
            var dir = string.IsNullOrEmpty(subdirectory) ? WorkDir : GetSafePath(subdirectory);
            if (!Directory.Exists(dir)) return $"Directory '{subdirectory}' not found.";
            
            var files = Directory.GetFileSystemEntries(dir)
                .Select(f => new FileInfo(f))
                .Select(fi => $"{(fi.Attributes.HasFlag(FileAttributes.Directory) ? "[DIR] " : "")}{fi.Name} ({fi.Length:N0} bytes, {fi.LastWriteTime:g})")
                .ToList();
            return files.Count == 0 ? "Workspace is empty." : string.Join("\n", files);
        }
        catch (Exception ex) { return $"Error listing files: {ex.Message}"; }
    }

    [KernelFunction, Description("Append text to an existing file")]
    public async Task<string> AppendFileAsync(
        [Description("Filename")] string filename,
        [Description("Text to append")] string content)
    {
        try
        {
            var path = GetSafePath(filename);
            await File.AppendAllTextAsync(path, content);
            return $"Appended {content.Length} chars to '{filename}'.";
        }
        catch (Exception ex) { return $"Error appending: {ex.Message}"; }
    }

    [KernelFunction, Description("Delete a file or directory")]
    public string Delete([Description("Filename or directory name")] string path)
    {
        try
        {
            var fullPath = GetSafePath(path);
            if (File.Exists(fullPath)) { File.Delete(fullPath); return $"File '{path}' deleted."; }
            if (Directory.Exists(fullPath)) { Directory.Delete(fullPath, true); return $"Directory '{path}' deleted recursively."; }
            return $"Path '{path}' not found.";
        }
        catch (Exception ex) { return $"Error deleting: {ex.Message}"; }
    }

    [KernelFunction, Description("Move or rename a file/directory")]
    public string Move(
        [Description("Source path")] string source,
        [Description("Destination path")] string destination)
    {
        try
        {
            var src = GetSafePath(source);
            var dest = GetSafePath(destination);
            if (File.Exists(src)) { File.Move(src, dest); return $"File moved to '{destination}'."; }
            if (Directory.Exists(src)) { Directory.Move(src, dest); return $"Directory moved to '{destination}'."; }
            return $"Source '{source}' not found.";
        }
        catch (Exception ex) { return $"Error moving: {ex.Message}"; }
    }

    private string GetSafePath(string filename)
    {
        var full = Path.GetFullPath(Path.Combine(WorkDir, filename));
        if (!full.StartsWith(WorkDir))
            throw new UnauthorizedAccessException("Access outside workspace is not allowed.");
        return full;
    }
}

// ─────────────────────────────────────────────────────────────
// SUMMARIZE SKILL
// ─────────────────────────────────────────────────────────────
public class SummarizeSkill
{
    private readonly Kernel _kernel;

    public SummarizeSkill(Kernel kernel) => _kernel = kernel;

    [KernelFunction, Description("Summarize a long text into key points")]
    public async Task<string> SummarizeAsync(
        [Description("Text to summarize")] string text,
        [Description("Maximum length of summary in words")] int maxWords = 150)
    {
        var prompt = $"Please summarize the following text in at most {maxWords} words, as bullet points:\n\n{text}";
        var result = await _kernel.InvokePromptAsync(prompt);
        return result.GetValue<string>() ?? "";
    }

    [KernelFunction, Description("Extract key information from text")]
    public async Task<string> ExtractKeyInfoAsync(
        [Description("Text to process")] string text,
        [Description("Type of info to extract: entities, dates, numbers, actions")] string extractType = "entities")
    {
        var prompt = $"Extract {extractType} from the following text. Return as a JSON list:\n\n{text}";
        var result = await _kernel.InvokePromptAsync(prompt);
        return result.GetValue<string>() ?? "";
    }
}

// ─────────────────────────────────────────────────────────────
// TRANSLATE SKILL
// ─────────────────────────────────────────────────────────────
public class TranslateSkill
{
    private readonly Kernel _kernel;

    public TranslateSkill(Kernel kernel) => _kernel = kernel;

    [KernelFunction, Description("Translate text from one language to another")]
    public async Task<string> TranslateAsync(
        [Description("Text to translate")] string text,
        [Description("Target language (e.g., Indonesian, English, French)")] string targetLanguage,
        [Description("Source language (auto-detect if empty)")] string sourceLanguage = "")
    {
        var fromPart = string.IsNullOrEmpty(sourceLanguage) ? "" : $" from {sourceLanguage}";
        var prompt = $"Translate the following text{fromPart} to {targetLanguage}. Return only the translation:\n\n{text}";
        var result = await _kernel.InvokePromptAsync(prompt);
        return result.GetValue<string>() ?? "";
    }

    [KernelFunction, Description("Detect the language of a text")]
    public async Task<string> DetectLanguageAsync([Description("Text to detect")] string text)
    {
        var prompt = $"Detect the language of this text and return only the language name:\n\n{text}";
        var result = await _kernel.InvokePromptAsync(prompt);
        return result.GetValue<string>() ?? "";
    }
}

// ─────────────────────────────────────────────────────────────
// SEARCH SKILL  
// ─────────────────────────────────────────────────────────────
public class SearchSkill
{
    private readonly AppConfiguration _config;
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public SearchSkill(AppConfiguration config) => _config = config;

    [KernelFunction, Description("Search the web for information")]
    public async Task<string> SearchWebAsync(
        [Description("Search query")] string query,
        [Description("Number of results (1-10)")] int numResults = 5)
    {
        numResults = Math.Clamp(numResults, 1, 10);
        return _config.Plugins.Search.Provider.ToLower() switch
        {
            "bing" => await SearchBingAsync(query, numResults),
            "google" => await SearchGoogleAsync(query, numResults),
            _ => await SearchDuckDuckGoAsync(query)
        };
    }

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
        if (!doc.RootElement.TryGetProperty("webPages", out var webPages)) return "No results found.";
        var results = webPages.GetProperty("value");

        var sb = new System.Text.StringBuilder();
        foreach (var r in results.EnumerateArray().Take(n))
        {
            sb.AppendLine($"• {r.GetProperty("name").GetString()}");
            sb.AppendLine($"  URL: {r.GetProperty("url").GetString()}");
            sb.AppendLine($"  {r.GetProperty("snippet").GetString()}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private async Task<string> SearchGoogleAsync(string query, int n)
    {
        if (string.IsNullOrEmpty(_config.Plugins.Search.GoogleApiKey))
            return "Google API key not configured.";

        var url = $"https://www.googleapis.com/customsearch/v1?key={_config.Plugins.Search.GoogleApiKey}" +
                  $"&cx={_config.Plugins.Search.GoogleCseId}&q={Uri.EscapeDataString(query)}&num={n}";

        var res = await _http.GetStringAsync(url);
        var doc = System.Text.Json.JsonDocument.Parse(res);
        if (!doc.RootElement.TryGetProperty("items", out var items)) return "No results found.";

        var sb = new System.Text.StringBuilder();
        foreach (var item in items.EnumerateArray().Take(n))
        {
            sb.AppendLine($"• {item.GetProperty("title").GetString()}");
            sb.AppendLine($"  URL: {item.GetProperty("link").GetString()}");
            sb.AppendLine($"  {item.GetProperty("snippet").GetString()}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private async Task<string> SearchDuckDuckGoAsync(string query)
    {
        var url = $"https://api.duckduckgo.com/?q={Uri.EscapeDataString(query)}&format=json&no_html=1";
        var res = await _http.GetStringAsync(url);
        var doc = System.Text.Json.JsonDocument.Parse(res);

        var abstract_ = doc.RootElement.GetProperty("Abstract").GetString();
        var relatedTopics = doc.RootElement.GetProperty("RelatedTopics");

        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(abstract_))
            sb.AppendLine($"Summary: {abstract_}\n");

        foreach (var topic in relatedTopics.EnumerateArray().Take(5))
        {
            if (topic.TryGetProperty("Text", out var text))
                sb.AppendLine($"• {text.GetString()}");
        }
        return sb.Length > 0 ? sb.ToString() : "No results found.";
    }
}

// ─────────────────────────────────────────────────────────────
// EMAIL SKILL
// ─────────────────────────────────────────────────────────────
public class EmailSkill
{
    private readonly AppConfiguration _config;

    public EmailSkill(AppConfiguration config) => _config = config;

    [KernelFunction, Description("Send an email via SMTP")]
    public async Task<string> SendEmailAsync(
        [Description("Recipient email address")] string to,
        [Description("Email subject")] string subject,
        [Description("Email body (plain text)")] string body,
        [Description("CC email addresses (comma-separated)")] string cc = "")
    {
        try
        {
            var smtp = _config.Plugins.Email;
            if (string.IsNullOrEmpty(smtp.SmtpHost))
                return "SMTP not configured. Set Plugins:Email:SmtpHost in app.config.";

            using var client = new System.Net.Mail.SmtpClient(smtp.SmtpHost, smtp.SmtpPort)
            {
                EnableSsl = smtp.UseSsl,
                Credentials = new System.Net.NetworkCredential(smtp.SmtpUser, smtp.SmtpPassword)
            };

            var mail = new System.Net.Mail.MailMessage(smtp.SmtpUser, to, subject, body);
            if (!string.IsNullOrEmpty(cc))
                foreach (var addr in cc.Split(','))
                    mail.CC.Add(addr.Trim());

            await client.SendMailAsync(mail);
            return $"Email sent successfully to {to}.";
        }
        catch (Exception ex) { return $"Failed to send email: {ex.Message}"; }
    }
}
*/
// ─────────────────────────────────────────────────────────────
// PROCESS SKILL (OpenClaw: exec, process)
// ─────────────────────────────────────────────────────────────
public class ProcessSkill
{
    [KernelFunction, Description("Execute a shell command")]
    public async Task<string> ExecuteCommandAsync(
        [Description("Command to execute")] string command,
        [Description("Arguments")] string arguments = "")
    {
        try
        {
            var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            var shell = isWindows ? "cmd.exe" : "/bin/bash";
            var shellArgs = isWindows ? $"/c \"{command} {arguments}\"" : $"-c \"{command} {arguments}\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = shell,
                Arguments = shellArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return "Failed to start process.";

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            if (await Task.WhenAny(Task.Delay(TimeSpan.FromSeconds(30)), process.WaitForExitAsync()) == Task.Delay(TimeSpan.FromSeconds(30)))
            {
                process.Kill();
                return "Process timed out.";
            }

            var output = await outputTask;
            var error = await errorTask;

            return $"Exit Code: {process.ExitCode}\nOutput: {output}\nError: {error}";
        }
        catch (Exception ex) { return $"Error executing command: {ex.Message}"; }
    }

    [KernelFunction, Description("List running processes")]
    public string ListProcesses()
    {
        try
        {
            var processes = Process.GetProcesses()
                .OrderByDescending(p => { try { return p.WorkingSet64; } catch { return 0; } })
                .Take(20)
                .Select(p => $"{p.Id, 6} {p.ProcessName,-20} {p.WorkingSet64 / 1024 / 1024, 8} MB")
                .ToList();

            return "Top 20 Processes by Memory:\nPID    Name                 Memory\n" + string.Join("\n", processes);
        }
        catch (Exception ex) { return $"Error listing processes: {ex.Message}"; }
    }

    [KernelFunction, Description("Kill a process by ID")]
    public string KillProcess([Description("Process ID")] int pid)
    {
        try
        {
            var p = Process.GetProcessById(pid);
            p.Kill();
            return $"Process {pid} ({p.ProcessName}) killed.";
        }
        catch (Exception ex) { return $"Error killing process: {ex.Message}"; }
    }
}
/*
// ─────────────────────────────────────────────────────────────
// SYSTEM SKILL
// ─────────────────────────────────────────────────────────────
public class SystemSkill
{
    [KernelFunction, Description("Get system information (OS, CPU, Memory)")]
    public string GetSystemInfo()
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
            sb.AppendLine($"Framework: {RuntimeInformation.FrameworkDescription}");
            sb.AppendLine($"Processors: {Environment.ProcessorCount}");
            sb.AppendLine($"Machine Name: {Environment.MachineName}");
            sb.AppendLine($"User: {Environment.UserName}");
            sb.AppendLine($"Uptime: {TimeSpan.FromMilliseconds(Environment.TickCount64):d' days 'h' hours 'm' minutes'}");
            
            return sb.ToString();
        }
        catch (Exception ex) { return $"Error getting system info: {ex.Message}"; }
    }

    [KernelFunction, Description("Get value of an environment variable")]
    public string GetEnvVar([Description("Variable name")] string name)
    {
        return Environment.GetEnvironmentVariable(name) ?? "Variable not found.";
    }
}
*/
// ─────────────────────────────────────────────────────────────
// WEB SCRAPER SKILL (OpenClaw: web_fetch)
// ─────────────────────────────────────────────────────────────
public class WebScraperSkill : HttpSkill
{
    [KernelFunction, Description("Fetch a web page and return cleaned text (no HTML)")]
    public async Task<string> FetchCleanTextAsync([Description("URL to fetch")] string url)
    {
        try
        {
            var html = await _http.GetStringAsync(url);
            
            // Basic HTML cleaning
            var text = Regex.Replace(html, "<script.*?</script>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<style.*?</style>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<.*?>", " ");
            text = Regex.Replace(text, @"\s+", " ").Trim();
            
            return text.Length > 10000 ? text[..10000] + "..." : text;
        }
        catch (Exception ex) { return $"Scraping Error: {ex.Message}"; }
    }

    [KernelFunction, Description("Extract links from a web page")]
    public async Task<string> ExtractLinksAsync([Description("URL to fetch")] string url)
    {
        try
        {
            var html = await _http.GetStringAsync(url);
            var matches = Regex.Matches(html, @"href=""(http.*?)""", RegexOptions.IgnoreCase);
            var links = matches.Select(m => m.Groups[1].Value).Distinct().Take(20).ToList();
            return links.Count == 0 ? "No links found." : string.Join("\n", links);
        }
        catch (Exception ex) { return $"Error extracting links: {ex.Message}"; }
    }
}

// ─────────────────────────────────────────────────────────────
// GITHUB SKILL
// ─────────────────────────────────────────────────────────────
public class GitHubSkill
{
    private static readonly HttpClient _http = new();
    
    public GitHubSkill()
    {
        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
            _http.DefaultRequestHeaders.Add("User-Agent", "SKClaw-Agent");
    }

    [KernelFunction, Description("Search GitHub repositories")]
    public async Task<string> SearchReposAsync([Description("Search query")] string query)
    {
        try
        {
            var url = $"https://api.github.com/search/repositories?q={Uri.EscapeDataString(query)}&per_page=5";
            var response = await _http.GetFromJsonAsync<System.Text.Json.JsonElement>(url);
            var items = response.GetProperty("items");

            var sb = new System.Text.StringBuilder();
            foreach (var item in items.EnumerateArray())
            {
                sb.AppendLine($"• {item.GetProperty("full_name").GetString()} ({item.GetProperty("stargazers_count").GetInt32()} stars)");
                sb.AppendLine($"  {item.GetProperty("description").GetString()}");
                sb.AppendLine($"  URL: {item.GetProperty("html_url").GetString()}");
                sb.AppendLine();
            }
            return sb.Length > 0 ? sb.ToString() : "No repositories found.";
        }
        catch (Exception ex) { return $"GitHub Error: {ex.Message}"; }
    }

    [KernelFunction, Description("Get content of a file from a GitHub repository")]
    public async Task<string> GetFileContentAsync(
        [Description("Owner/Repo (e.g., microsoft/semantic-kernel)")] string repo,
        [Description("Path to file")] string path,
        [Description("Branch/Ref (default: main)")] string @ref = "main")
    {
        try
        {
            var url = $"https://raw.githubusercontent.com/{repo}/{@ref}/{path}";
            var content = await _http.GetStringAsync(url);
            return content.Length > 8000 ? content[..8000] + "..." : content;
        }
        catch (Exception ex) { return $"Error fetching file: {ex.Message}"; }
    }
}
/*
// ─────────────────────────────────────────────────────────────
// PLUGIN REGISTRY
// ─────────────────────────────────────────────────────────────
public class PluginRegistry
{
    public static void RegisterAll(Kernel kernel, AppConfiguration config)
    {
        var enabled = config.Plugins.EnabledSkills;
        var all = enabled.Length == 0;

        if (all || enabled.Contains("TimeSkill"))
            kernel.ImportPluginFromObject(new TimeSkill(), "Time");

        if (all || enabled.Contains("MathSkill"))
            kernel.ImportPluginFromObject(new MathSkill(), "Math");

        if (all || enabled.Contains("HttpSkill"))
            kernel.ImportPluginFromObject(new HttpSkill(), "Http");

        if (all || enabled.Contains("FileSkill"))
            kernel.ImportPluginFromObject(new FileSkill(), "File");

        if (all || enabled.Contains("SummarizeSkill"))
            kernel.ImportPluginFromObject(new SummarizeSkill(kernel), "Summarize");

        if (all || enabled.Contains("TranslateSkill"))
            kernel.ImportPluginFromObject(new TranslateSkill(kernel), "Translate");

        if (all || enabled.Contains("SearchSkill"))
            kernel.ImportPluginFromObject(new SearchSkill(config), "Search");

        if (all || enabled.Contains("EmailSkill"))
            kernel.ImportPluginFromObject(new EmailSkill(config), "Email");

        // NEW SKILLS
        if (all || enabled.Contains("ProcessSkill"))
            kernel.ImportPluginFromObject(new ProcessSkill(), "Process");

        if (all || enabled.Contains("SystemSkill"))
            kernel.ImportPluginFromObject(new SystemSkill(), "System");

        if (all || enabled.Contains("WebScraperSkill"))
            kernel.ImportPluginFromObject(new WebScraperSkill(), "WebScraper");

        if (all || enabled.Contains("GitHubSkill"))
            kernel.ImportPluginFromObject(new GitHubSkill(), "GitHub");
    }
}
*/
