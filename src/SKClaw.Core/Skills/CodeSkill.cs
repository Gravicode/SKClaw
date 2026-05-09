using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.SemanticKernel;
using SKClaw.Core.Configuration;

namespace SKClaw.Core.Skills;

/// <summary>
/// CodeSkill — AI-powered code generation, review, explanation, refactoring,
/// documentation, test generation, and sandboxed execution.
/// </summary>
public class CodeSkill
{
    private readonly Kernel _kernel;
    private readonly AppConfiguration _config;

    public CodeSkill(Kernel kernel, AppConfiguration config)
    {
        _kernel = kernel;
        _config = config;
    }

    // ── AI Code Generation ─────────────────────────────────────

    [KernelFunction, Description("Generate code in any programming language based on a description")]
    public async Task<string> GenerateCodeAsync(
        [Description("Description of what the code should do")] string description,
        [Description("Programming language: python, csharp, javascript, typescript, go, rust, java, sql, bash, etc.")] string language = "python",
        [Description("Style: function, class, script, api, cli")] string style = "function")
    {
        return await Prompt($"""
            Write {style} code in {language} that does the following: {description}
            
            Requirements:
            - Write clean, well-commented, production-quality code
            - Include error handling
            - Add type hints/annotations where applicable
            - Return ONLY the code block, no explanation before or after
            """);
    }

    [KernelFunction, Description("Review code for bugs, security issues, and improvements")]
    public async Task<string> ReviewCodeAsync(
        [Description("Source code to review")] string code,
        [Description("Programming language (auto-detect if empty)")] string language = "",
        [Description("Review focus: all, security, performance, style, bugs")] string focus = "all")
    {
        return await Prompt($"""
            Perform a {focus} code review for this {language} code.
            
            Provide structured feedback on:
            1. 🐛 Bugs & Logic Errors
            2. 🔒 Security Vulnerabilities
            3. ⚡ Performance Issues
            4. 📝 Code Quality & Readability
            5. 🧪 Testability
            6. ✅ Rating (1-10) with summary
            
            Code:
            ```{language}
            {code}
            ```
            """);
    }

    [KernelFunction, Description("Explain what a piece of code does, line by line if needed")]
    public async Task<string> ExplainCodeAsync(
        [Description("Code to explain")] string code,
        [Description("Explanation level: beginner, intermediate, expert")] string level = "intermediate",
        [Description("Include line-by-line breakdown?")] bool lineByLine = false)
    {
        var detail = lineByLine ? " Provide a line-by-line breakdown." : "";
        return await Prompt($"""
            Explain the following code for a {level} programmer.{detail}
            Cover: purpose, how it works, inputs/outputs, edge cases, and complexity.
            
            Code:
            {code}
            """);
    }

    [KernelFunction, Description("Refactor code to improve quality, readability, or performance")]
    public async Task<string> RefactorCodeAsync(
        [Description("Code to refactor")] string code,
        [Description("Refactoring goal: readable, performance, solid_principles, dry, patterns, modernize")] string goal = "readable",
        [Description("Programming language")] string language = "")
    {
        return await Prompt($"""
            Refactor this {language} code with focus on: {goal}
            
            Return:
            1. The refactored code (in a code block)
            2. A bullet list of changes made and why
            
            Original code:
            {code}
            """);
    }

    [KernelFunction, Description("Generate unit tests for a function or class")]
    public async Task<string> GenerateTestsAsync(
        [Description("Code to write tests for")] string code,
        [Description("Testing framework: xunit, nunit, pytest, jest, junit, go_test")] string framework = "pytest",
        [Description("Coverage: basic, edge_cases, full")] string coverage = "edge_cases")
    {
        return await Prompt($"""
            Generate {coverage} unit tests using {framework} for the following code.
            Cover: happy path, edge cases, null/empty inputs, boundary values, error conditions.
            Return ONLY the test code.
            
            Code to test:
            {code}
            """);
    }

    [KernelFunction, Description("Generate API documentation (docstring, JSDoc, XML doc) for code")]
    public async Task<string> GenerateDocsAsync(
        [Description("Code to document")] string code,
        [Description("Doc format: docstring (Python), jsdoc (JS/TS), xmldoc (C#), javadoc (Java)")] string format = "docstring")
    {
        return await Prompt($"""
            Generate complete {format} documentation for this code.
            Include: description, parameters, return value, exceptions, examples.
            Return the code with documentation added inline.
            
            Code:
            {code}
            """);
    }

    [KernelFunction, Description("Convert code from one programming language to another")]
    public async Task<string> TranslateCodeAsync(
        [Description("Source code")] string code,
        [Description("Source language")] string fromLanguage,
        [Description("Target language")] string toLanguage)
    {
        return await Prompt($"""
            Translate this {fromLanguage} code to {toLanguage}.
            Preserve functionality exactly. Use idiomatic {toLanguage} patterns and conventions.
            Add comments where the translation required non-obvious decisions.
            
            Original {fromLanguage} code:
            {code}
            """);
    }

    [KernelFunction, Description("Fix a bug in code based on an error message or description")]
    public async Task<string> FixBugAsync(
        [Description("Buggy code")] string code,
        [Description("Error message or bug description")] string errorDescription,
        [Description("Programming language")] string language = "")
    {
        return await Prompt($"""
            Fix the following bug in this {language} code.
            
            Error/Problem: {errorDescription}
            
            Provide:
            1. Root cause explanation
            2. Fixed code (in a code block)
            3. What was changed and why
            
            Buggy code:
            {code}
            """);
    }

    [KernelFunction, Description("Generate a SQL query from a natural language description")]
    public async Task<string> GenerateSqlAsync(
        [Description("What the query should do")] string description,
        [Description("Table schema as JSON or description")] string schema = "",
        [Description("SQL dialect: generic, postgresql, mysql, sqlserver, sqlite, oracle")] string dialect = "generic")
    {
        return await Prompt($"""
            Generate a {dialect} SQL query that: {description}
            {(string.IsNullOrEmpty(schema) ? "" : $"\nSchema:\n{schema}")}
            
            Return: the SQL query with inline comments explaining each clause.
            Include an optimisation note if applicable.
            """);
    }

    [KernelFunction, Description("Generate a regular expression for a pattern description")]
    public async Task<string> GenerateRegexAsync(
        [Description("Pattern to match, e.g. 'valid email address', 'US phone number', 'ISO date'")] string description,
        [Description("Regex flavour: javascript, python, dotnet, pcre")] string flavour = "pcre",
        [Description("Include test examples?")] bool withExamples = true)
    {
        return await Prompt($"""
            Generate a {flavour} regular expression to match: {description}
            
            Return:
            1. The regex pattern
            2. Explanation of each part
            {(withExamples ? "3. 5 matching examples\n4. 3 non-matching examples" : "")}
            """);
    }

    [KernelFunction, Description("Analyse code complexity, structure, and suggest architecture improvements")]
    public async Task<string> AnalyseCodeAsync(
        [Description("Code to analyse")] string code,
        [Description("Language")] string language = "")
    {
        return await Prompt($"""
            Perform a static analysis of this {language} code. Report on:
            
            1. Cyclomatic complexity estimate
            2. Code smell detection (long methods, deep nesting, duplicates)
            3. SOLID principle violations
            4. Dependency analysis
            5. Architecture suggestions
            6. Overall maintainability score (A-F)
            
            Code:
            {code}
            """);
    }

    // ── Sandboxed Execution ────────────────────────────────────

    [KernelFunction, Description("Execute a Python script in a sandboxed subprocess (if Python is installed and code execution is enabled in config)")]
    public async Task<string> ExecutePythonAsync(
        [Description("Python script to execute")] string script,
        [Description("Timeout in seconds")] int timeoutSeconds = 10)
    {
        if (!_config.Plugins.Code.Enabled)
            return "Code execution is disabled. Set Plugins:Code:Enabled=true in app.config.";
        if (!_config.Plugins.Code.AllowedLanguages.Contains("python"))
            return "Python execution is not in Plugins:Code:AllowedLanguages.";
        return await RunScriptAsync("python3", script, ".py", timeoutSeconds);
    }

    [KernelFunction, Description("Execute a JavaScript/Node.js script in a sandboxed subprocess (if Node is installed)")]
    public async Task<string> ExecuteJavaScriptAsync(
        [Description("JavaScript code to execute")] string script,
        [Description("Timeout in seconds")] int timeoutSeconds = 10)
    {
        if (!_config.Plugins.Code.Enabled)
            return "Code execution is disabled. Set Plugins:Code:Enabled=true in app.config.";
        if (!_config.Plugins.Code.AllowedLanguages.Contains("javascript"))
            return "JavaScript execution is not in Plugins:Code:AllowedLanguages.";
        return await RunScriptAsync("node", script, ".js", timeoutSeconds);
    }

    [KernelFunction, Description("Execute a C# script using dotnet-script or csi (if installed)")]
    public async Task<string> ExecuteCSharpAsync(
        [Description("C# script (using dotnet-script CSX format)")] string script,
        [Description("Timeout in seconds")] int timeoutSeconds = 30)
    {
        if (!_config.Plugins.Code.Enabled)
            return "Code execution is disabled. Set Plugins:Code:Enabled=true in app.config.";
        if (!_config.Plugins.Code.AllowedLanguages.Contains("csharp"))
            return "C# execution is not in Plugins:Code:AllowedLanguages.";
        return await RunScriptAsync("dotnet-script", script, ".csx", timeoutSeconds);
    }

    [KernelFunction, Description("Execute a Bash/Shell script (Linux/macOS only)")]
    public async Task<string> ExecuteBashAsync(
        [Description("Bash script")] string script,
        [Description("Timeout in seconds")] int timeoutSeconds = 15)
    {
        if (!_config.Plugins.Code.Enabled)
            return "Code execution is disabled.";
        if (OperatingSystem.IsWindows())
            return "Bash is not available on Windows. Use PowerShell instead.";
        return await RunScriptAsync("/bin/bash", script, ".sh", timeoutSeconds);
    }

    // ── Diff & Analysis ────────────────────────────────────────

    [KernelFunction, Description("Generate a diff between two code snippets and explain the changes")]
    public async Task<string> DiffCodeAsync(
        [Description("Original code")] string original,
        [Description("Modified code")] string modified,
        [Description("Language")] string language = "")
    {
        var lines1 = original.Split('\n');
        var lines2 = modified.Split('\n');
        var sb = new StringBuilder();
        int maxLen = Math.Max(lines1.Length, lines2.Length);
        int changes = 0;
        for (int i = 0; i < maxLen; i++)
        {
            var a = i < lines1.Length ? lines1[i] : null;
            var b = i < lines2.Length ? lines2[i] : null;
            if (a != b)
            {
                changes++;
                if (a != null) sb.AppendLine($"- {a}");
                if (b != null) sb.AppendLine($"+ {b}");
            }
        }
        string diff = changes == 0 ? "No differences." : sb.ToString();
        var explanation = await Prompt($"Explain these code changes in plain English:\n\n{diff}");
        return $"=== DIFF ({changes} changed lines) ===\n{diff}\n\n=== EXPLANATION ===\n{explanation}";
    }

    [KernelFunction, Description("Calculate code metrics: lines of code, comments, blank lines, comment ratio")]
    public string CalculateMetrics(
        [Description("Source code")] string code,
        [Description("Language for comment syntax: python, csharp, javascript, java, cpp, bash")] string language = "python")
    {
        var lines = code.Split('\n');
        string singleComment = language.ToLower() switch
        {
            "python" or "bash" or "ruby" => "#",
            "sql" => "--",
            _ => "//"
        };

        int totalLines = lines.Length;
        int blankLines = lines.Count(l => string.IsNullOrWhiteSpace(l));
        int commentLines = lines.Count(l => l.Trim().StartsWith(singleComment));
        int codeLines = totalLines - blankLines - commentLines;
        double commentRatio = totalLines > 0 ? (double)commentLines / totalLines * 100 : 0;

        var words = Regex.Matches(code, @"\b\w+\b").Count;
        var functions = Regex.Matches(code, language.ToLower() switch
        {
            "python" => @"\bdef\s+\w+",
            "javascript" or "typescript" => @"\bfunction\s+\w+|\b\w+\s*=\s*\(.*?\)\s*=>",
            "csharp" or "java" => @"\b(public|private|protected|internal|static)\b.*\b\w+\s*\(",
            _ => @"\bfunction\b|\bdef\b|\bfunc\b"
        }).Count;

        return $"""
            Language      : {language}
            Total Lines   : {totalLines}
            Code Lines    : {codeLines}
            Comment Lines : {commentLines} ({commentRatio:F1}%)
            Blank Lines   : {blankLines}
            Word Count    : {words}
            Functions Est.: {functions}
            Char Count    : {code.Length}
            """;
    }

    // ── Private helpers ────────────────────────────────────────
    private async Task<string> RunScriptAsync(string interpreter, string script, string ext, int timeout)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"skclaw_{Guid.NewGuid():N}{ext}");
        try
        {
            await File.WriteAllTextAsync(tmp, script);
            var psi = new ProcessStartInfo(interpreter, tmp)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi)!;
            var outTask = proc.StandardOutput.ReadToEndAsync();
            var errTask = proc.StandardError.ReadToEndAsync();
            bool done = proc.WaitForExit(timeout * 1000);
            if (!done) { proc.Kill(true); return "⏱️ Execution timed out."; }

            var stdout = await outTask;
            var stderr = await errTask;
            var maxOut = _config.Plugins.Code.MaxOutputBytes;

            var result = new StringBuilder();
            if (!string.IsNullOrEmpty(stdout))
                result.Append(stdout.Length > maxOut ? stdout[..maxOut] + "\n...[truncated]" : stdout);
            if (!string.IsNullOrEmpty(stderr))
                result.AppendLine($"\n[STDERR]\n{(stderr.Length > 2000 ? stderr[..2000] : stderr)}");
            result.AppendLine($"\n[Exit: {proc.ExitCode}]");
            return result.ToString().Trim();
        }
        catch (Exception ex) { return $"Execution error: {ex.Message}"; }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    private async Task<string> Prompt(string text)
    {
        var result = await _kernel.InvokePromptAsync(text);
        return result.GetValue<string>() ?? "";
    }
}
