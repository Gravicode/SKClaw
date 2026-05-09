using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using SKClaw.Core.Configuration;

namespace SKClaw.Core.Skills;

/// <summary>
/// PluginRegistry — Central registration point for all built-in and optional skills.
/// Reads the enabled list from app.config (Plugins:EnabledSkills).
/// Pass "*" or leave empty to enable ALL skills.
/// </summary>
public static class PluginRegistry
{
    /// <summary>
    /// Register all configured plugins into the Kernel.
    /// </summary>
    public static void RegisterAll(Kernel kernel, AppConfiguration config,
        ILogger? logger = null)
    {
        var enabled = config.Plugins.EnabledSkills;
        bool all = enabled.Length == 0 || (enabled.Length == 1 && enabled[0] == "*");

        int count = 0;

        void Register(string name, Func<object> factory, string pluginName)
        {
            if (!all && !enabled.Any(e => e.Equals(name, StringComparison.OrdinalIgnoreCase))) return;
            try
            {
                kernel.ImportPluginFromObject(factory(), pluginName);
                count++;
                logger?.LogDebug("Registered plugin: {Plugin}", pluginName);
            }
            catch (Exception ex)
            {
                logger?.LogWarning("Failed to register plugin {Plugin}: {Error}", pluginName, ex.Message);
            }
        }

        // ── Core Skills (always fast, no external deps) ────────────────
        Register("TimeSkill",       () => new TimeSkill(),                  "Time");
        Register("MathSkill",       () => new MathSkill(),                  "Math");
        Register("TextSkill",       () => new TextSkill(),                  "Text");
        Register("DataSkill",       () => new DataSkill(),                  "Data");
        Register("SystemSkill",     () => new SystemSkill(),                "System");
        Register("FileSkill",       () => new FileSkill(),                  "File");
        Register("HttpSkill",       () => new HttpSkill(),                  "Http");

        // ── AI-powered Skills (require LLM) ────────────────────────────
        Register("SummarizeSkill",  () => new SummarizeSkill(kernel),       "Summarize");
        Register("TranslateSkill",  () => new TranslateSkill(kernel),       "Translate");
        Register("ReasoningSkill",  () => new ReasoningSkill(kernel),       "Reasoning");
        Register("ContentSkill",    () => new ContentSkill(kernel),         "Content");
        Register("CodeSkill",       () => new CodeSkill(kernel, config),    "Code");

        // ── Communication Skills ───────────────────────────────────────
        Register("EmailSkill",       () => new EmailSkill(config),          "Email");
        Register("NotificationSkill",() => new NotificationSkill(),         "Notify");
        Register("SearchSkill",      () => new SearchSkill(config),         "Search");

        // NEW SKILLS
        Register("ProcessSkill", () => new ProcessSkill(), "Search");
        Register("SystemSkill", () => new SystemSkill(), "Search");
        Register("WebScraperSkill", () => new WebScraperSkill(), "Search");
        Register("GitHubSkill", () => new GitHubSkill(), "Search");

        logger?.LogInformation("PluginRegistry: {Count} plugin(s) registered", count);
    }

    /// <summary>
    /// Register a single custom plugin at runtime.
    /// </summary>
    public static void RegisterCustom(Kernel kernel, object plugin, string name)
    {
        kernel.ImportPluginFromObject(plugin, name);
    }

    /// <summary>
    /// Get a list of all registered plugin names and function counts.
    /// </summary>
    public static IEnumerable<(string Plugin, int Functions)> GetRegisteredPlugins(Kernel kernel)
        => kernel.Plugins.Select(p => (p.Name, p.Count()));
}
