using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using SKClaw.Core.Configuration;

namespace SKClaw.Plugins;

/// <summary>
/// ExtraPluginRegistry — Registers all SKClaw.Plugins extra tools into the Kernel.
/// Call RegisterAll() after the core PluginRegistry to add these community tools.
/// Configuration is taken from app.config (Plugins:EnabledSkills list).
/// </summary>
public static class ExtraPluginRegistry
{
    /// <summary>
    /// Register all extra plugins into the kernel.
    /// Each plugin can be individually enabled/disabled via Plugins:EnabledSkills in app.config.
    /// </summary>
    public static void RegisterAll(Kernel kernel, AppConfiguration config, ILogger? logger = null)
    {
        var enabled = config.Plugins.EnabledSkills;
        bool all    = enabled.Length == 0 || (enabled.Length == 1 && enabled[0] == "*");

        int count = 0;

        void Register(string name, Func<object> factory, string pluginName)
        {
            if (!all && !enabled.Any(e => e.Equals(name, StringComparison.OrdinalIgnoreCase))) return;
            try
            {
                kernel.ImportPluginFromObject(factory(), pluginName);
                count++;
                logger?.LogDebug("[ExtraPlugins] Registered: {Plugin}", pluginName);
            }
            catch (Exception ex)
            {
                logger?.LogWarning("[ExtraPlugins] Failed to register {Plugin}: {Error}", pluginName, ex.Message);
            }
        }

        // ── Finance & Markets ──────────────────────────────────────
        Register("FinancePlugin",      () => new Tools.FinancePlugin(),              "Finance");

        // ── Geo / Weather ──────────────────────────────────────────
        Register("GeoPlugin",          () => new Tools.GeoPlugin(),                  "Geo");

        // ── Media & AI Vision ─────────────────────────────────────
        Register("MediaPlugin",        () => new Tools.MediaPlugin(
            openAiKey    : config.LLM.OpenAI.ApiKey,
            stabilityKey : ""),                                                       "Media");

        // ── Database ───────────────────────────────────────────────
        Register("DatabasePlugin",     () => new Tools.DatabasePlugin(kernel),       "Database");

        // ── DevOps & Git ───────────────────────────────────────────
        Register("DevOpsPlugin",       () => new Tools.DevOpsPlugin(),               "DevOps");

        // ── Productivity & Tasks ───────────────────────────────────
        Register("ProductivityPlugin", () => new Tools.ProductivityPlugin(),         "Productivity");

        // ── Health & Fitness ───────────────────────────────────────
        Register("HealthPlugin",       () => new Tools.HealthPlugin(kernel),         "Health");

        // ── Web & RSS ──────────────────────────────────────────────
        Register("WebPlugin",          () => new Tools.WebPlugin(kernel),            "Web");

        // ── Security & Crypto ──────────────────────────────────────
        Register("SecurityPlugin",     () => new Tools.SecurityPlugin(),             "Security");

        logger?.LogInformation("[ExtraPlugins] {Count} extra plugin(s) registered", count);
    }

    /// <summary>
    /// Register a single extra plugin at runtime.
    /// Useful for registering plugins dynamically (e.g. from a user plugin directory).
    /// </summary>
    public static void RegisterCustom(Kernel kernel, object pluginInstance, string pluginName)
    {
        kernel.ImportPluginFromObject(pluginInstance, pluginName);
    }

    /// <summary>
    /// Return metadata for all extra plugins (for documentation/admin UI).
    /// </summary>
    public static IReadOnlyList<PluginMeta> GetAllPluginMeta() =>
    [
        new("FinancePlugin",      "Finance",      "Stock prices, crypto, forex, financial calculators"),
        new("GeoPlugin",          "Geo",          "Weather, geocoding, distance, maps, nearby places"),
        new("MediaPlugin",        "Media",        "Image generation (DALL-E/SD), vision, OCR, TTS, Whisper"),
        new("DatabasePlugin",     "Database",     "SQLite CRUD, Text-to-SQL, CSV import/export"),
        new("DevOpsPlugin",       "DevOps",       "Git, GitHub API, Docker, CI/CD code generation"),
        new("ProductivityPlugin", "Productivity", "Tasks, notes, habits, goals, Pomodoro"),
        new("HealthPlugin",       "Health",       "BMI, BMR, nutrition, workout, sleep, hydration"),
        new("WebPlugin",          "Web",          "RSS feeds, web scraping, URL monitoring, sitemap"),
        new("SecurityPlugin",     "Security",     "Password gen, encryption, JWT, HMAC, security scan"),
    ];
}

public record PluginMeta(string ClassName, string KernelName, string Description);
