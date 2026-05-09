using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.SemanticKernel;

namespace SKClaw.Core.Skills;

/// <summary>
/// TextSkill — Full text/string processing: analysis, manipulation,
/// encoding, hashing, regex, extraction, and formatting.
/// </summary>
public class TextSkill
{
    // ── Analysis ───────────────────────────────────────────────

    [KernelFunction, Description("Analyse text: word count, char count, sentences, paragraphs, reading time, lexical diversity")]
    public string AnalyseText([Description("Text to analyse")] string text)
    {
        if (string.IsNullOrEmpty(text)) return "Empty text.";
        var words = Regex.Split(text.Trim(), @"\s+").Where(w => w.Length > 0).ToArray();
        var sentences = Regex.Split(text, @"[.!?]+\s*").Where(s => s.Trim().Length > 0).Count();
        var paragraphs = text.Split(["\n\n", "\r\n\r\n"], StringSplitOptions.RemoveEmptyEntries).Length;
        var unique = words.Select(w => w.ToLower()).Distinct().Count();
        double readingMin = words.Length / 200.0;
        return $"""
            Characters  : {text.Length} ({text.Count(c => c != ' ' && c != '\n')} non-space)
            Words       : {words.Length}
            Unique words: {unique} (lexical diversity: {(double)unique/words.Length:P1})
            Sentences   : {sentences}
            Paragraphs  : {paragraphs}
            Lines       : {text.Split('\n').Length}
            Reading time: ~{readingMin:F1} min ({(int)(readingMin * 60)} sec)
            Avg word len: {words.Average(w => w.Length):F1} chars
            """;
    }

    [KernelFunction, Description("Count occurrences of a word or pattern in text")]
    public string CountOccurrences(
        [Description("Text to search in")] string text,
        [Description("Word or regex pattern to count")] string pattern,
        [Description("Case sensitive? true or false")] bool caseSensitive = false)
    {
        var opts = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
        var matches = Regex.Matches(text, Regex.Escape(pattern), opts);
        return $"'{pattern}' found {matches.Count} time(s) in the text.";
    }

    [KernelFunction, Description("Find all regex matches in a text")]
    public string RegexFind(
        [Description("Text to search")] string text,
        [Description("Regex pattern")] string pattern,
        [Description("Return first N matches (0 = all)")] int maxResults = 0)
    {
        try
        {
            var matches = Regex.Matches(text, pattern);
            var list = maxResults > 0 ? matches.Take(maxResults) : matches.Cast<Match>();
            var results = list.Select((m, i) => $"[{i+1}] pos={m.Index} len={m.Length}: \"{m.Value}\"").ToList();
            if (results.Count == 0) return "No matches found.";
            return $"{matches.Count} match(es):\n" + string.Join("\n", results);
        }
        catch (Exception ex) { return $"Regex error: {ex.Message}"; }
    }

    [KernelFunction, Description("Extract emails, URLs, phone numbers, dates, or IP addresses from text")]
    public string ExtractPatterns(
        [Description("Text to extract from")] string text,
        [Description("Type: email, url, phone, date, ip, hashtag, mention, number")] string type)
    {
        string pattern = type.ToLower() switch
        {
            "email" => @"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}",
            "url" => @"https?://[^\s<>""]+[^\s<>"".,;!?)]",
            "phone" => @"[\+]?[(]?[0-9]{1,4}[)]?[-\s\.]?[0-9]{1,4}[-\s\.]?[0-9]{4,6}",
            "date" => @"\b\d{1,4}[-/\.]\d{1,2}[-/\.]\d{2,4}\b",
            "ip" => @"\b(?:\d{1,3}\.){3}\d{1,3}\b",
            "hashtag" => @"#\w+",
            "mention" => @"@\w+",
            "number" => @"-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?",
            _ => "Unknown type. Use: email, url, phone, date, ip, hashtag, mention, number"
        };
        var matches = Regex.Matches(text, pattern).Select(m => m.Value).Distinct().ToList();
        return matches.Count == 0 ? $"No {type}s found." : $"Found {matches.Count} {type}(s):\n" + string.Join("\n", matches);
    }

    // ── Transformation ─────────────────────────────────────────

    [KernelFunction, Description("Change text case: upper, lower, title, sentence, camel, pascal, snake, kebab, constant")]
    public string ChangeCase(
        [Description("Input text")] string text,
        [Description("Case type: upper, lower, title, sentence, camel, pascal, snake, kebab, constant, toggle")] string caseType)
    {
        return caseType.ToLower() switch
        {
            "upper"    => text.ToUpper(),
            "lower"    => text.ToLower(),
            "title"    => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text.ToLower()),
            "sentence" => char.ToUpper(text[0]) + text[1..].ToLower(),
            "camel"    => ToCamel(text),
            "pascal"   => ToPascal(text),
            "snake"    => ToSnake(text),
            "kebab"    => ToKebab(text),
            "constant" => ToSnake(text).ToUpper(),
            "toggle"   => new string(text.Select(c => char.IsUpper(c) ? char.ToLower(c) : char.ToUpper(c)).ToArray()),
            _          => "Unknown case type."
        };
    }

    [KernelFunction, Description("Search and replace text with optional regex support")]
    public string Replace(
        [Description("Original text")] string text,
        [Description("Search pattern")] string search,
        [Description("Replacement text")] string replacement,
        [Description("Use regex? true/false")] bool useRegex = false,
        [Description("Case sensitive? true/false")] bool caseSensitive = true)
    {
        if (useRegex)
        {
            var opts = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
            return Regex.Replace(text, search, replacement, opts);
        }
        return caseSensitive
            ? text.Replace(search, replacement)
            : Regex.Replace(text, Regex.Escape(search), replacement, RegexOptions.IgnoreCase);
    }

    [KernelFunction, Description("Trim, pad, truncate, or wrap text")]
    public string FormatText(
        [Description("Input text")] string text,
        [Description("Operation: trim, ltrim, rtrim, pad_left, pad_right, pad_center, truncate, wrap, indent")] string operation,
        [Description("Width or length for padding/truncation/wrapping")] int width = 80,
        [Description("Padding character or ellipsis")] string padChar = " ")
    {
        char pad = padChar.Length > 0 ? padChar[0] : ' ';
        return operation.ToLower() switch
        {
            "trim"       => text.Trim(),
            "ltrim"      => text.TrimStart(),
            "rtrim"      => text.TrimEnd(),
            "pad_left"   => text.PadLeft(width, pad),
            "pad_right"  => text.PadRight(width, pad),
            "pad_center" => text.PadLeft((width + text.Length) / 2, pad).PadRight(width, pad),
            "truncate"   => text.Length <= width ? text : text[..Math.Max(0, width - 3)] + "...",
            "wrap"       => WordWrap(text, width),
            "indent"     => string.Join("\n", text.Split('\n').Select(l => new string(' ', width) + l)),
            _ => "Unknown operation."
        };
    }

    [KernelFunction, Description("Sort lines of text alphabetically, by length, or reverse")]
    public string SortLines(
        [Description("Multi-line text to sort")] string text,
        [Description("Sort by: alpha, length, reverse, shuffle")] string by = "alpha",
        [Description("Remove duplicate lines?")] bool dedupe = false)
    {
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r'));
        if (dedupe) lines = lines.Distinct();
        var sorted = by.ToLower() switch
        {
            "alpha"   => lines.OrderBy(l => l, StringComparer.OrdinalIgnoreCase),
            "length"  => lines.OrderBy(l => l.Length),
            "reverse" => lines.Reverse(),
            "shuffle" => lines.OrderBy(_ => Random.Shared.Next()),
            _         => lines
        };
        return string.Join("\n", sorted);
    }

    [KernelFunction, Description("Split text into chunks by delimiter, line, sentence or fixed size")]
    public string SplitText(
        [Description("Text to split")] string text,
        [Description("Split by: newline, sentence, word, char, or a custom delimiter string")] string by,
        [Description("Max items to return (0=all)")] int limit = 0)
    {
        string[] parts = by.ToLower() switch
        {
            "newline"  => text.Split('\n'),
            "sentence" => Regex.Split(text, @"(?<=[.!?])\s+"),
            "word"     => Regex.Split(text.Trim(), @"\s+"),
            "char"     => text.Select(c => c.ToString()).ToArray(),
            _          => text.Split(by)
        };
        if (limit > 0) parts = parts.Take(limit).ToArray();
        return $"Split into {parts.Length} parts:\n" +
               string.Join("\n", parts.Select((p, i) => $"[{i+1}] {p}"));
    }

    // ── Encoding / Decoding ────────────────────────────────────

    [KernelFunction, Description("Encode or decode text: base64, url, html, hex, rot13, binary")]
    public string Encode(
        [Description("Input text or encoded string")] string input,
        [Description("Encoding: base64, base64url, url, html, hex, rot13, binary, unicode")] string encoding,
        [Description("Direction: encode or decode")] string direction = "encode")
    {
        bool enc = direction.ToLower() == "encode";
        return encoding.ToLower() switch
        {
            "base64"    => enc ? Convert.ToBase64String(Encoding.UTF8.GetBytes(input))
                               : Encoding.UTF8.GetString(Convert.FromBase64String(input)),
            "base64url" => enc ? Convert.ToBase64String(Encoding.UTF8.GetBytes(input)).Replace('+','-').Replace('/','_').TrimEnd('=')
                               : Encoding.UTF8.GetString(Convert.FromBase64String(input.Replace('-','+').Replace('_','/') + new string('=', (4 - input.Length % 4) % 4))),
            "url"       => enc ? Uri.EscapeDataString(input) : Uri.UnescapeDataString(input),
            "html"      => enc ? System.Web.HttpUtility.HtmlEncode(input) : System.Web.HttpUtility.HtmlDecode(input),
            "hex"       => enc ? Convert.ToHexString(Encoding.UTF8.GetBytes(input))
                               : Encoding.UTF8.GetString(Convert.FromHexString(input)),
            "rot13"     => new string(input.Select(c =>
                c >= 'a' && c <= 'z' ? (char)((c - 'a' + 13) % 26 + 'a') :
                c >= 'A' && c <= 'Z' ? (char)((c - 'A' + 13) % 26 + 'A') : c).ToArray()),
            "binary"    => enc ? string.Join(" ", Encoding.UTF8.GetBytes(input).Select(b => Convert.ToString(b, 2).PadLeft(8, '0')))
                               : Encoding.UTF8.GetString(input.Split(' ').Select(s => Convert.ToByte(s, 2)).ToArray()),
            "unicode"   => enc ? string.Join("", input.Select(c => $"\\u{(int)c:X4}"))
                               : Regex.Unescape(input),
            _ => "Unknown encoding."
        };
    }

    // ── Hashing ────────────────────────────────────────────────

    [KernelFunction, Description("Compute hash of text: MD5, SHA1, SHA256, SHA384, SHA512, HMAC-SHA256")]
    public string Hash(
        [Description("Input text")] string text,
        [Description("Algorithm: md5, sha1, sha256, sha384, sha512")] string algorithm = "sha256",
        [Description("Output format: hex (default) or base64")] string outputFormat = "hex")
    {
        byte[] inputBytes = Encoding.UTF8.GetBytes(text);
        byte[] hash = algorithm.ToLower() switch
        {
            "md5"    => MD5.HashData(inputBytes),
            "sha1"   => SHA1.HashData(inputBytes),
            "sha256" => SHA256.HashData(inputBytes),
            "sha384" => SHA384.HashData(inputBytes),
            "sha512" => SHA512.HashData(inputBytes),
            _ => throw new ArgumentException($"Unknown algorithm: {algorithm}")
        };
        return outputFormat.ToLower() == "base64"
            ? Convert.ToBase64String(hash)
            : Convert.ToHexString(hash).ToLower();
    }

    // ── String utilities ───────────────────────────────────────

    [KernelFunction, Description("Generate a random string: UUID, alphanumeric, PIN, passphrase")]
    public string GenerateString(
        [Description("Type: uuid, guid, alphanumeric, numeric, hex, pin, slug")] string type = "uuid",
        [Description("Length (for alphanumeric, numeric, hex, pin types)")] int length = 16)
    {
        return type.ToLower() switch
        {
            "uuid" or "guid" => Guid.NewGuid().ToString(),
            "alphanumeric"   => RandomString("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789", length),
            "numeric" or "pin" => RandomString("0123456789", length),
            "hex"            => RandomString("0123456789abcdef", length),
            "slug"           => RandomString("abcdefghijklmnopqrstuvwxyz0123456789-", length),
            _                => Guid.NewGuid().ToString()
        };
    }

    [KernelFunction, Description("Compare two strings and calculate similarity (Levenshtein distance and ratio)")]
    public string CompareStrings(
        [Description("First string")] string a,
        [Description("Second string")] string b)
    {
        int dist = LevenshteinDistance(a, b);
        double ratio = 1.0 - (double)dist / Math.Max(a.Length, b.Length);
        bool isAnagram = a.Length == b.Length &&
            a.ToLower().OrderBy(c => c).SequenceEqual(b.ToLower().OrderBy(c => c));
        bool isPalindrome_a = a == new string(a.Reverse().ToArray());
        bool isPalindrome_b = b == new string(b.Reverse().ToArray());
        return $"""
            String A: "{a}" (len={a.Length})
            String B: "{b}" (len={b.Length})
            Levenshtein distance : {dist}
            Similarity ratio     : {ratio:P1}
            Are anagrams         : {isAnagram}
            A is palindrome      : {isPalindrome_a}
            B is palindrome      : {isPalindrome_b}
            """;
    }

    [KernelFunction, Description("Repeat, reverse, or interleave strings")]
    public string ManipulateString(
        [Description("Input text")] string text,
        [Description("Operation: repeat, reverse, palindrome_check, shuffle, caesar")] string operation,
        [Description("Count (for repeat) or shift (for caesar)")] int param = 2)
    {
        return operation.ToLower() switch
        {
            "repeat"          => string.Concat(Enumerable.Repeat(text, param)),
            "reverse"         => new string(text.Reverse().ToArray()),
            "palindrome_check"=> text == new string(text.Reverse().ToArray()) ? "Yes, it's a palindrome." : "No, not a palindrome.",
            "shuffle"         => new string(text.OrderBy(_ => Random.Shared.Next()).ToArray()),
            "caesar"          => new string(text.Select(c =>
                char.IsLetter(c) ? (char)(((c + param - (char.IsUpper(c) ? 'A' : 'a')) % 26 + 26) % 26 + (char.IsUpper(c) ? 'A' : 'a')) : c).ToArray()),
            _ => "Unknown operation."
        };
    }

    [KernelFunction, Description("Convert text to slug, title, or normalize whitespace")]
    public string NormalizeText(
        [Description("Text to normalize")] string text,
        [Description("Mode: slug (URL-safe), whitespace (collapse spaces), lines (normalize newlines), accents (remove diacritics)")] string mode)
    {
        return mode.ToLower() switch
        {
            "slug"       => Regex.Replace(text.ToLower().Trim(), @"[^a-z0-9]+", "-").Trim('-'),
            "whitespace" => Regex.Replace(text.Trim(), @"\s+", " "),
            "lines"      => Regex.Replace(text, @"\r\n|\r", "\n"),
            "accents"    => RemoveDiacritics(text),
            _ => "Mode must be: slug, whitespace, lines, accents"
        };
    }

    // ── Private helpers ────────────────────────────────────────
    private static string ToCamel(string s) { var p = ToPascal(s); return p.Length == 0 ? p : char.ToLower(p[0]) + p[1..]; }
    private static string ToPascal(string s) => Regex.Replace(s, @"(?:^|[\s_\-]+)(\w)", m => m.Groups[1].Value.ToUpper());
    private static string ToSnake(string s) => Regex.Replace(Regex.Replace(s, @"([A-Z]+)([A-Z][a-z])", "$1_$2"), @"([a-z\d])([A-Z])", "$1_$2").ToLower().Replace(" ", "_").Replace("-", "_");
    private static string ToKebab(string s) => ToSnake(s).Replace('_', '-');
    private static string WordWrap(string text, int width)
    {
        var sb = new StringBuilder();
        foreach (var para in text.Split('\n'))
        {
            var words = para.Split(' ');
            var line  = new StringBuilder();
            foreach (var w in words)
            {
                if (line.Length + w.Length + 1 > width) { sb.AppendLine(line.ToString().TrimEnd()); line.Clear(); }
                if (line.Length > 0) line.Append(' ');
                line.Append(w);
            }
            if (line.Length > 0) sb.AppendLine(line.ToString());
        }
        return sb.ToString().TrimEnd();
    }
    private static string RandomString(string chars, int len) =>
        new(Enumerable.Repeat(chars, len).Select(s => s[Random.Shared.Next(s.Length)]).ToArray());
    private static int LevenshteinDistance(string a, string b)
    {
        int[,] d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                d[i, j] = a[i - 1] == b[j - 1] ? d[i-1, j-1] : 1 + Math.Min(d[i-1, j-1], Math.Min(d[i-1, j], d[i, j-1]));
        return d[a.Length, b.Length];
    }
    private static string RemoveDiacritics(string text)
    {
        var norm = text.Normalize(NormalizationForm.FormD);
        return new string(norm.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Normalize(NormalizationForm.FormC);
    }
}
