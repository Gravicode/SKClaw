using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Linq;
using Microsoft.SemanticKernel;

namespace SKClaw.Plugins.Tools;

/// <summary>
/// DevOpsPlugin — Git operations, GitHub/GitLab API integration,
/// Docker inspection, and general DevOps utilities.
/// </summary>
public class DevOpsPlugin
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly string _githubToken;
    private readonly string _gitlabToken;
    private readonly string _gitlabUrl;

    public DevOpsPlugin(string githubToken = "", string gitlabToken = "", string gitlabUrl = "https://gitlab.com")
    {
        _githubToken = githubToken;
        _gitlabToken = gitlabToken;
        _gitlabUrl   = gitlabUrl;
    }

    // ── Git (local) ────────────────────────────────────────────

    [KernelFunction, Description("Get the status of a local git repository")]
    public async Task<string> GitStatusAsync(
        [Description("Path to the git repository (empty = current directory)")] string repoPath = "")
    {
        return await RunGitAsync(repoPath, "status --short --branch");
    }

    [KernelFunction, Description("Get the recent commit log of a git repository")]
    public async Task<string> GitLogAsync(
        [Description("Repository path (empty = current directory)")] string repoPath = "",
        [Description("Number of commits to show")] int count = 10,
        [Description("Show file changes? true/false")] bool showFiles = false)
    {
        var format = showFiles
            ? $"log --oneline --name-status -{count}"
            : $"log --oneline --decorate -{count}";
        return await RunGitAsync(repoPath, format);
    }

    [KernelFunction, Description("Get git diff for uncommitted changes or between two commits/branches")]
    public async Task<string> GitDiffAsync(
        [Description("Repository path (empty = current directory)")] string repoPath = "",
        [Description("Reference A (empty = working tree)")] string refA = "",
        [Description("Reference B (empty = HEAD)")] string refB = "")
    {
        var args = string.IsNullOrEmpty(refA) && string.IsNullOrEmpty(refB)
            ? "diff --stat"
            : string.IsNullOrEmpty(refB)
                ? $"diff {refA}"
                : $"diff {refA} {refB}";
        var result = await RunGitAsync(repoPath, args + " --stat");
        return result;
    }

    [KernelFunction, Description("List branches in a local git repository")]
    public async Task<string> GitBranchesAsync(
        [Description("Repository path")] string repoPath = "",
        [Description("Show remote branches too?")] bool showRemote = false)
    {
        return await RunGitAsync(repoPath, showRemote ? "branch -a --color=never" : "branch --color=never");
    }

    [KernelFunction, Description("Get git stash list for a repository")]
    public async Task<string> GitStashListAsync([Description("Repository path")] string repoPath = "")
        => await RunGitAsync(repoPath, "stash list");

    [KernelFunction, Description("Generate a git commit message based on staged diff (AI-powered)")]
    public async Task<string> GenerateCommitMessageAsync(
        [Description("Repository path")] string repoPath = "",
        [Description("Commit style: conventional, imperative, descriptive")] string style = "conventional")
    {
        var diff = await RunGitAsync(repoPath, "diff --staged --stat");
        if (diff.Contains("no changes") || string.IsNullOrWhiteSpace(diff))
            return "No staged changes found. Stage files with 'git add' first.";

        var detailedDiff = await RunGitAsync(repoPath, "diff --staged");
        // Truncate for LLM context
        if (detailedDiff.Length > 6000) detailedDiff = detailedDiff[..6000] + "\n...[truncated]";

        return $"""
            Based on the following git diff, generate a {style} commit message.
            
            Git Diff Summary:
            {diff}
            
            Detailed Diff:
            {detailedDiff}
            
            Return ONLY the commit message (subject line + optional body). No extra text.
            """;
    }

    // ── GitHub API ─────────────────────────────────────────────

    [KernelFunction, Description("Get information about a GitHub repository")]
    public async Task<string> GetGithubRepoAsync(
        [Description("Repository in owner/repo format, e.g. microsoft/semantic-kernel")] string repo)
    {
        var res = await GithubApiAsync($"repos/{repo}");
        if (res == null) return "Repository not found or API error.";

        var doc = res.Value;
        return $"""
            📦 {doc.GetProperty("full_name").GetString()}
            Description: {doc.GetProperty("description").GetString()}
            Stars   : {doc.GetProperty("stargazers_count").GetInt32():N0}
            Forks   : {doc.GetProperty("forks_count").GetInt32():N0}
            Issues  : {doc.GetProperty("open_issues_count").GetInt32():N0}
            Language: {doc.GetProperty("language").GetString()}
            License : {(doc.TryGetProperty("license", out var lic) && lic.ValueKind != JsonValueKind.Null ? lic.GetProperty("spdx_id").GetString() : "none")}
            Topics  : {string.Join(", ", doc.GetProperty("topics").EnumerateArray().Select(t => t.GetString()))}
            Created : {doc.GetProperty("created_at").GetString()}
            Updated : {doc.GetProperty("updated_at").GetString()}
            URL     : {doc.GetProperty("html_url").GetString()}
            """;
    }

    [KernelFunction, Description("List open issues or pull requests in a GitHub repository")]
    public async Task<string> ListGithubIssuesAsync(
        [Description("Repository in owner/repo format")] string repo,
        [Description("Type: issues or pulls")] string type = "issues",
        [Description("State: open, closed, all")] string state = "open",
        [Description("Max results")] int maxResults = 10)
    {
        var path = type == "pulls" ? $"repos/{repo}/pulls?state={state}&per_page={maxResults}" : $"repos/{repo}/issues?state={state}&per_page={maxResults}";
        var arr = await GithubApiArrayAsync(path);
        if (arr == null) return "Could not fetch issues.";

        var sb = new StringBuilder($"📋 {repo} — {type} ({state}):\n\n");
        foreach (var issue in arr.Take(maxResults))
        {
            var num   = issue.GetProperty("number").GetInt32();
            var title = issue.GetProperty("title").GetString();
            var user  = issue.GetProperty("user").GetProperty("login").GetString();
            var labels = string.Join(", ", issue.GetProperty("labels").EnumerateArray().Select(l => l.GetProperty("name").GetString()));
            sb.AppendLine($"#{num} [{user}] {title}{(string.IsNullOrEmpty(labels) ? "" : $" [{labels}]")}");
        }
        return sb.ToString().TrimEnd();
    }

    [KernelFunction, Description("Search GitHub repositories by keyword")]
    public async Task<string> SearchGithubAsync(
        [Description("Search query")] string query,
        [Description("Sort: stars, forks, updated")] string sort = "stars",
        [Description("Max results")] int maxResults = 10)
    {
        var path = $"search/repositories?q={Uri.EscapeDataString(query)}&sort={sort}&per_page={maxResults}";
        var res = await GithubApiAsync(path);
        if (res == null) return "Search failed.";

        var doc = res.Value;
        var items = doc.GetProperty("items");
        var sb = new StringBuilder($"🔍 GitHub search '{query}':\n\n");
        foreach (var item in items.EnumerateArray().Take(maxResults))
        {
            var name  = item.GetProperty("full_name").GetString();
            var stars = item.GetProperty("stargazers_count").GetInt32();
            var desc  = item.GetProperty("description").GetString() ?? "";
            var lang  = item.GetProperty("language").GetString() ?? "";
            sb.AppendLine($"⭐{stars,7:N0} | {name,-40} | {lang,-15} | {desc[..Math.Min(60, desc.Length)]}");
        }
        return sb.ToString().TrimEnd();
    }

    [KernelFunction, Description("Get the latest releases for a GitHub repository")]
    public async Task<string> GetGithubReleasesAsync(
        [Description("Repository in owner/repo format")] string repo,
        [Description("Max releases to return")] int count = 5)
    {
        var arr = await GithubApiArrayAsync($"repos/{repo}/releases?per_page={count}");
        if (arr == null) return "Could not fetch releases.";

        var sb = new StringBuilder($"🚀 {repo} — Latest Releases:\n\n");
        foreach (var rel in arr.Take(count))
        {
            var tag  = rel.GetProperty("tag_name").GetString();
            var name = rel.GetProperty("name").GetString();
            var date = rel.GetProperty("published_at").GetString();
            var pre  = rel.GetProperty("prerelease").GetBoolean();
            sb.AppendLine($"  {tag} — {name}{(pre ? " [pre-release]" : "")} ({date?[..10]})");
        }
        return sb.ToString().TrimEnd();
    }

    // ── Docker ─────────────────────────────────────────────────

    [KernelFunction, Description("List running Docker containers")]
    public async Task<string> DockerPsAsync(
        [Description("Show all containers including stopped ones?")] bool all = false)
    {
        return await RunCommandAsync("docker", all ? "ps -a --format table" : "ps --format table");
    }

    [KernelFunction, Description("List Docker images on the local machine")]
    public async Task<string> DockerImagesAsync()
        => await RunCommandAsync("docker", "images --format table");

    [KernelFunction, Description("Get stats (CPU, memory) for running containers")]
    public async Task<string> DockerStatsAsync()
        => await RunCommandAsync("docker", "stats --no-stream --format table");

    [KernelFunction, Description("Get logs from a Docker container")]
    public async Task<string> DockerLogsAsync(
        [Description("Container name or ID")] string container,
        [Description("Number of lines to return")] int lines = 50)
        => await RunCommandAsync("docker", $"logs --tail {lines} {container}");

    [KernelFunction, Description("Get detailed info about a Docker container or image")]
    public async Task<string> DockerInspectAsync(
        [Description("Container or image name/ID")] string target)
    {
        var json = await RunCommandAsync("docker", $"inspect {target}");
        try
        {
            var doc = JsonDocument.Parse(json);
            var item = doc.RootElement[0];
            var name   = item.TryGetProperty("Name", out var n) ? n.GetString()?.TrimStart('/') : target;
            var status = item.TryGetProperty("State", out var s) ? s.GetProperty("Status").GetString() : "unknown";
            var image  = item.TryGetProperty("Config", out var cfg) ? cfg.GetProperty("Image").GetString() : "";
            return $"Container: {name}\nStatus: {status}\nImage: {image}";
        }
        catch { return json[..Math.Min(2000, json.Length)]; }
    }

    // ── CI/CD Helpers ──────────────────────────────────────────

    [KernelFunction, Description("Generate a GitHub Actions workflow YAML from a description")]
    public async Task<string> GenerateGithubActionsAsync(
        [Description("Describe the CI/CD workflow you need")] string description,
        [Description("Main language/runtime: node, python, dotnet, java, go, rust, docker")] string runtime = "node")
    {
        // This delegates to LLM via caller, returning prompt for the agent to use
        return $"""
            Generate a complete GitHub Actions workflow YAML file for: {description}
            Runtime/language: {runtime}
            
            Include:
            - Trigger events (push, pull_request)
            - Checkout step
            - Setup {runtime} step
            - Install dependencies
            - Run tests
            - Build step
            - Appropriate caching
            
            Return only the YAML content.
            """;
    }

    [KernelFunction, Description("Generate a Dockerfile for a project")]
    public async Task<string> GenerateDockerfileAsync(
        [Description("Project description")] string description,
        [Description("Base technology: node, python, dotnet, java, go, nginx, alpine")] string baseImage = "node",
        [Description("Multi-stage build? true/false")] bool multiStage = true)
    {
        return $"""
            Generate an optimized Dockerfile for: {description}
            Base image: {baseImage}
            Multi-stage build: {multiStage}
            
            Best practices to follow:
            - Use official slim/alpine base images
            - Minimize layers
            - Non-root user
            - HEALTHCHECK instruction
            - Proper ENTRYPOINT vs CMD
            - .dockerignore considerations (mention in comments)
            
            Return only the Dockerfile content.
            """;
    }

    // ── Helpers ────────────────────────────────────────────────
    private async Task<JsonElement?> GithubApiAsync(string path)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/{path}");
        req.Headers.UserAgent.ParseAdd("SKClaw/1.0");
        if (!string.IsNullOrEmpty(_githubToken))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _githubToken);
        var res = await _http.SendAsync(req);
        if (!res.IsSuccessStatusCode) return null;
        var json = await res.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json).RootElement;
    }

    private async Task<JsonElement[]?> GithubApiArrayAsync(string path)
    {
        var el = await GithubApiAsync(path);
        if (el == null) return null;
        return el.Value.EnumerateArray().ToArray();
    }

    private static async Task<string> RunGitAsync(string repoPath, string args)
    {
        var dir = string.IsNullOrEmpty(repoPath) ? Directory.GetCurrentDirectory() : repoPath;
        var psi = new ProcessStartInfo("git", args)
        {
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        try
        {
            using var proc = Process.Start(psi)!;
            var stdout = await proc.StandardOutput.ReadToEndAsync();
            var stderr = await proc.StandardError.ReadToEndAsync();
            proc.WaitForExit();
            var result = (stdout + stderr).Trim();
            return string.IsNullOrEmpty(result) ? "(no output)" : result;
        }
        catch (Exception ex) { return $"git error: {ex.Message}"; }
    }

    private static async Task<string> RunCommandAsync(string cmd, string args)
    {
        var psi = new ProcessStartInfo(cmd, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        try
        {
            using var proc = Process.Start(psi)!;
            var stdout = await proc.StandardOutput.ReadToEndAsync();
            proc.WaitForExit(10000);
            return string.IsNullOrEmpty(stdout) ? "(no output)" : stdout.Trim();
        }
        catch (Exception ex) { return $"Command error: {ex.Message}"; }
    }
}
