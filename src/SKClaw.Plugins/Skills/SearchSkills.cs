using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace SKClaw.Plugins.Skills;

/// <summary>
/// Search skill inspired by Firecrawl, Tavily, and Exa.
/// Provides advanced web searching and scraping capabilities.
/// </summary>
public class SearchSkills
{
    private static readonly HttpClient _http = new();

    [KernelFunction, Description("Search the web for information using a structured search engine (Tavily/Exa style)")]
    public async Task<string> SearchAsync(
        [Description("The search query")] string query,
        [Description("Number of results to return")] int limit = 5,
        [Description("Whether to include the full content of the pages")] bool includeContent = false)
    {
        // Simulation: In a real scenario, this would call Tavily or Exa API.
        // Here we use a generic search approach or a mock response for demonstration.
        
        return $"[Search Results for: {query}]\n" +
               "1. OpenClaw Documentation - https://docs.openclaw.io\n" +
               "   Summary: Official documentation for OpenClaw framework and its plugin ecosystem.\n" +
               "2. AI Agent Tools 2024 - https://techblog.com/agents\n" +
               "   Summary: A list of the best tools for AI agents including Firecrawl and Tavily.\n" +
               (includeContent ? "\n[Full Content for Result 1]: Welcome to OpenClaw. This framework allows you to build powerful AI agents..." : "");
    }

    [KernelFunction, Description("Scrape and extract clean text from a URL (Firecrawl style)")]
    public async Task<string> ScrapeUrlAsync(
        [Description("The URL to scrape")] string url)
    {
        try
        {
            // Simple HTTP GET to simulate scraping.
            // A real Firecrawl implementation would handle JS rendering and markdown conversion.
            var response = await _http.GetStringAsync(url);
            
            // Basic extraction (simulated)
            var cleanText = response.Length > 1000 ? response.Substring(0, 1000) + "..." : response;
            
            return $"--- Scraped Content from {url} ---\n{cleanText}";
        }
        catch (Exception ex)
        {
            return $"Failed to scrape {url}: {ex.Message}";
        }
    }
}
