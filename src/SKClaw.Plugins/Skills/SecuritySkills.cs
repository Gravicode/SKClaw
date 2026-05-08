using System.ComponentModel;
using System.Text.RegularExpressions;
using Microsoft.SemanticKernel;

namespace SKClaw.Plugins.Skills;

/// <summary>
/// Security auditing skill inspired by SecureClaw.
/// Scans code and configurations for potential vulnerabilities.
/// </summary>
public class SecuritySkills
{
    [KernelFunction, Description("Audit code for common security vulnerabilities (OWASP patterns)")]
    public string AuditCode(
        [Description("The code to audit")] string code,
        [Description("Programming language")] string language)
    {
        var findings = new List<string>();

        // Simple Regex-based security checks
        if (Regex.IsMatch(code, @"Password\s*=\s*""[^""]+""", RegexOptions.IgnoreCase))
            findings.Add("⚠️ Hardcoded password detected.");
        
        if (Regex.IsMatch(code, @"SELECT .* FROM .* WHERE .* = '"" \+ .*", RegexOptions.IgnoreCase))
            findings.Add("🔴 Potential SQL Injection vulnerability (string concatenation in query).");

        if (Regex.IsMatch(code, @"eval\(", RegexOptions.IgnoreCase))
            findings.Add("🔴 Dangerous use of 'eval()' function detected.");

        if (Regex.IsMatch(code, @"http://", RegexOptions.IgnoreCase))
            findings.Add("⚠️ Insecure HTTP protocol used instead of HTTPS.");

        if (findings.Count == 0)
            return "✅ No obvious security issues found in the code snippet provided.";

        return "Security Audit Findings:\n" + string.Join("\n", findings);
    }

    [KernelFunction, Description("Analyze a URL for potential phishing or security risks")]
    public string CheckUrlSafety(
        [Description("The URL to check")] string url)
    {
        if (url.Contains(".exe") || url.Contains(".zip"))
            return "⚠️ Warning: This URL points to a downloadable file. Proceed with caution.";
        
        if (!url.StartsWith("https://"))
            return "⚠️ Warning: This site does not use an encrypted connection (HTTP).";

        if (url.Length > 100 && url.Count(c => c == '-') > 5)
            return "🚩 Suspicious: This URL has characteristics often found in phishing links.";

        return "✅ The URL appears to be safe based on basic heuristics.";
    }
}
