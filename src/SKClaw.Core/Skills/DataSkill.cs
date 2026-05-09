using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.SemanticKernel;

namespace SKClaw.Core.Skills;

/// <summary>
/// DataSkill — Data format conversion, querying, transformation, and validation.
/// Supports: JSON, XML, CSV, YAML-like, Markdown tables, Base64, and more.
/// </summary>
public class DataSkill
{
    private static readonly JsonSerializerOptions _prettyJson = new() { WriteIndented = true };

    // ── JSON ───────────────────────────────────────────────────

    [KernelFunction, Description("Pretty-print, minify, or validate a JSON string")]
    public string FormatJson(
        [Description("JSON string")] string json,
        [Description("Mode: pretty, minify, validate")] string mode = "pretty")
    {
        try
        {
            var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            return mode.ToLower() switch
            {
                "minify"   => JsonSerializer.Serialize(doc.RootElement),
                "validate" => "✅ Valid JSON",
                _          => JsonSerializer.Serialize(doc.RootElement, _prettyJson)
            };
        }
        catch (Exception ex) { return mode == "validate" ? $"❌ Invalid JSON: {ex.Message}" : $"JSON Error: {ex.Message}"; }
    }

    [KernelFunction, Description("Query a JSON object using a dot-notation path, e.g. 'user.address.city' or 'items[0].name'")]
    public string QueryJson(
        [Description("JSON string")] string json,
        [Description("Dot-notation path, e.g. data.users[0].name")] string path)
    {
        try
        {
            var node = JsonNode.Parse(json);
            var parts = Regex.Split(path, @"\.(?![^\[]*\])");
            foreach (var part in parts)
            {
                if (node == null) return "null";
                var indexMatch = Regex.Match(part, @"^(\w+)\[(\d+)\]$");
                if (indexMatch.Success)
                {
                    node = node[indexMatch.Groups[1].Value]?[int.Parse(indexMatch.Groups[2].Value)];
                }
                else if (Regex.IsMatch(part, @"^\[\d+\]$"))
                {
                    node = node[int.Parse(part.Trim('[', ']'))];
                }
                else
                {
                    node = node[part];
                }
            }
            return node?.ToJsonString(_prettyJson) ?? "null";
        }
        catch (Exception ex) { return $"Query error: {ex.Message}"; }
    }

    [KernelFunction, Description("Merge two JSON objects (second overrides first for duplicate keys)")]
    public string MergeJson(
        [Description("First JSON object")] string json1,
        [Description("Second JSON object (overrides conflicts)")] string json2)
    {
        try
        {
            var dict1 = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json1) ?? [];
            var dict2 = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json2) ?? [];
            foreach (var kv in dict2) dict1[kv.Key] = kv.Value;
            return JsonSerializer.Serialize(dict1, _prettyJson);
        }
        catch (Exception ex) { return $"Merge error: {ex.Message}"; }
    }

    [KernelFunction, Description("Get the keys, schema structure, or statistics of a JSON object")]
    public string AnalyseJson([Description("JSON string")] string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var sb = new StringBuilder();
            AnalyseElement(doc.RootElement, "", sb, 0);
            return sb.ToString();
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    [KernelFunction, Description("Convert a JSON array to a CSV table")]
    public string JsonToCsv(
        [Description("JSON array of objects")] string json,
        [Description("Include header row?")] bool header = true)
    {
        try
        {
            var arr = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json) ?? [];
            if (arr.Count == 0) return "Empty array.";
            var keys = arr.SelectMany(d => d.Keys).Distinct().ToList();
            var sb = new StringBuilder();
            if (header) sb.AppendLine(string.Join(",", keys.Select(CsvEscape)));
            foreach (var row in arr)
                sb.AppendLine(string.Join(",", keys.Select(k => row.TryGetValue(k, out var v) ? CsvEscape(v.ToString()) : "")));
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    [KernelFunction, Description("Convert a JSON array to a Markdown table")]
    public string JsonToMarkdownTable([Description("JSON array of objects")] string json)
    {
        try
        {
            var arr = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json) ?? [];
            if (arr.Count == 0) return "Empty array.";
            var keys = arr.SelectMany(d => d.Keys).Distinct().ToList();
            var sb = new StringBuilder();
            sb.AppendLine("| " + string.Join(" | ", keys) + " |");
            sb.AppendLine("|" + string.Concat(keys.Select(_ => "---|")) );
            foreach (var row in arr)
                sb.AppendLine("| " + string.Join(" | ", keys.Select(k => row.TryGetValue(k, out var v) ? v.ToString().Replace("|","\\|") : "")) + " |");
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    // ── CSV ────────────────────────────────────────────────────

    [KernelFunction, Description("Parse CSV and return as JSON array of objects")]
    public string CsvToJson(
        [Description("CSV string (first row = headers)")] string csv,
        [Description("Delimiter character")] string delimiter = ",")
    {
        try
        {
            var lines = csv.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
            if (lines.Count < 2) return "[]";
            char delim = delimiter.Length > 0 ? delimiter[0] : ',';
            var headers = ParseCsvLine(lines[0], delim);
            var result = new List<Dictionary<string, string>>();
            foreach (var line in lines.Skip(1))
            {
                var values = ParseCsvLine(line, delim);
                var row = new Dictionary<string, string>();
                for (int i = 0; i < headers.Count; i++)
                    row[headers[i]] = i < values.Count ? values[i] : "";
                result.Add(row);
            }
            return JsonSerializer.Serialize(result, _prettyJson);
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    [KernelFunction, Description("Filter CSV rows by column value condition")]
    public string FilterCsv(
        [Description("CSV string")] string csv,
        [Description("Column name to filter by")] string column,
        [Description("Operator: eq, ne, gt, lt, contains, startswith")] string op,
        [Description("Value to compare against")] string value)
    {
        try
        {
            var lines = csv.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
            if (lines.Count < 2) return "No data.";
            var headers = ParseCsvLine(lines[0], ',');
            int colIdx = headers.IndexOf(column);
            if (colIdx < 0) return $"Column '{column}' not found. Available: {string.Join(", ", headers)}";

            var filtered = lines.Skip(1).Where(line =>
            {
                var vals = ParseCsvLine(line, ',');
                var v = colIdx < vals.Count ? vals[colIdx] : "";
                return op.ToLower() switch
                {
                    "eq" => v.Equals(value, StringComparison.OrdinalIgnoreCase),
                    "ne" => !v.Equals(value, StringComparison.OrdinalIgnoreCase),
                    "gt" => double.TryParse(v, out var d1) && double.TryParse(value, out var d2) && d1 > d2,
                    "lt" => double.TryParse(v, out var d1) && double.TryParse(value, out var d2) && d1 < d2,
                    "contains"   => v.Contains(value, StringComparison.OrdinalIgnoreCase),
                    "startswith" => v.StartsWith(value, StringComparison.OrdinalIgnoreCase),
                    _ => false
                };
            }).ToList();

            return lines[0] + "\n" + string.Join("\n", filtered) + $"\n[{filtered.Count} rows matched]";
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    [KernelFunction, Description("Sort CSV data by a column")]
    public string SortCsv(
        [Description("CSV string")] string csv,
        [Description("Column name to sort by")] string column,
        [Description("Order: asc or desc")] string order = "asc",
        [Description("Treat values as numbers?")] bool numeric = false)
    {
        try
        {
            var lines = csv.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
            if (lines.Count < 2) return csv;
            var headers = ParseCsvLine(lines[0], ',');
            int colIdx = headers.IndexOf(column);
            if (colIdx < 0) return $"Column '{column}' not found.";

            var dataRows = lines.Skip(1).Select(l => ParseCsvLine(l, ',')).ToList();
            var sorted = numeric
                ? dataRows.OrderBy(r => double.TryParse(colIdx < r.Count ? r[colIdx] : "0", out var d) ? d : 0)
                : dataRows.OrderBy(r => colIdx < r.Count ? r[colIdx] : "", StringComparer.OrdinalIgnoreCase);

            if (order.ToLower() == "desc") sorted = (IOrderedEnumerable<List<string>>)sorted.Reverse();
            var result = new List<string> { lines[0] };
            result.AddRange(sorted.Select(r => string.Join(",", r.Select(CsvEscape))));
            return string.Join("\n", result);
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    [KernelFunction, Description("Get summary statistics for numeric columns in CSV data")]
    public string SummariseCsv([Description("CSV string with header row")] string csv)
    {
        try
        {
            var lines = csv.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
            if (lines.Count < 2) return "No data.";
            var headers = ParseCsvLine(lines[0], ',');
            var rows = lines.Skip(1).Select(l => ParseCsvLine(l, ',')).ToList();
            var sb = new StringBuilder($"Rows: {rows.Count}, Columns: {headers.Count}\n\n");

            for (int i = 0; i < headers.Count; i++)
            {
                var vals = rows.Select(r => i < r.Count ? r[i] : "").ToList();
                var nums = vals.Select(v => double.TryParse(v, out var d) ? (double?)d : null).Where(d => d.HasValue).Select(d => d!.Value).ToArray();
                if (nums.Length > 0)
                {
                    Array.Sort(nums);
                    sb.AppendLine($"[{headers[i]}] numeric ({nums.Length}/{vals.Count}):");
                    sb.AppendLine($"  min={nums.Min():G8}  max={nums.Max():G8}  mean={nums.Average():G8}  sum={nums.Sum():G8}");
                    sb.AppendLine($"  median={(nums.Length % 2 == 0 ? (nums[nums.Length/2-1]+nums[nums.Length/2])/2 : nums[nums.Length/2]):G8}");
                }
                else
                {
                    var uniq = vals.Distinct().Count();
                    sb.AppendLine($"[{headers[i]}] text ({uniq} unique values)");
                }
            }
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    // ── XML ────────────────────────────────────────────────────

    [KernelFunction, Description("Pretty-print, minify, or validate XML")]
    public string FormatXml(
        [Description("XML string")] string xml,
        [Description("Mode: pretty, minify, validate")] string mode = "pretty")
    {
        try
        {
            var doc = XDocument.Parse(xml);
            return mode.ToLower() switch
            {
                "minify"   => doc.ToString(SaveOptions.DisableFormatting),
                "validate" => "✅ Valid XML",
                _          => doc.ToString()
            };
        }
        catch (Exception ex) { return mode == "validate" ? $"❌ Invalid XML: {ex.Message}" : $"XML Error: {ex.Message}"; }
    }

    [KernelFunction, Description("Query XML using XPath expression")]
    public string QueryXml(
        [Description("XML string")] string xml,
        [Description("XPath expression, e.g. //item/title or /root/users/user[@id='1']")] string xpath)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var nodes = doc.SelectNodes(xpath);
            if (nodes == null || nodes.Count == 0) return "No nodes matched.";
            var sb = new StringBuilder($"Found {nodes.Count} node(s):\n");
            foreach (XmlNode node in nodes) sb.AppendLine(node.OuterXml);
            return sb.ToString();
        }
        catch (Exception ex) { return $"XPath error: {ex.Message}"; }
    }

    [KernelFunction, Description("Convert XML to JSON")]
    public string XmlToJson([Description("XML string")] string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var result = XmlNodeToDict(doc.Root!);
            return JsonSerializer.Serialize(result, _prettyJson);
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    [KernelFunction, Description("Convert JSON object to XML string")]
    public string JsonToXml(
        [Description("JSON object string")] string json,
        [Description("Root element name")] string rootElement = "root")
    {
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [];
            var root = new XElement(rootElement, dict.Select(kv => DictToXmlElement(kv.Key, kv.Value)));
            return new XDocument(root).ToString();
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    // ── Markdown Table ─────────────────────────────────────────

    [KernelFunction, Description("Convert a Markdown table to JSON or CSV")]
    public string MarkdownTableTo(
        [Description("Markdown table string")] string table,
        [Description("Output format: json or csv")] string format = "json")
    {
        try
        {
            var lines = table.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("|")).ToList();
            if (lines.Count < 3) return "Not a valid Markdown table (need header + separator + data rows).";
            var headers = lines[0].Trim('|').Split('|').Select(h => h.Trim()).ToList();
            var rows = lines.Skip(2).Select(l => l.Trim('|').Split('|').Select(c => c.Trim()).ToList()).ToList();

            if (format.ToLower() == "csv")
            {
                var sb = new StringBuilder(string.Join(",", headers.Select(CsvEscape)) + "\n");
                foreach (var row in rows)
                    sb.AppendLine(string.Join(",", headers.Select((_, i) => CsvEscape(i < row.Count ? row[i] : ""))));
                return sb.ToString().TrimEnd();
            }
            var result = rows.Select(row => headers.Select((h, i) => new { h, v = i < row.Count ? row[i] : "" }).ToDictionary(x => x.h, x => x.v));
            return JsonSerializer.Serialize(result, _prettyJson);
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    [KernelFunction, Description("Convert CSV to a Markdown table")]
    public string CsvToMarkdownTable(
        [Description("CSV string with headers")] string csv,
        [Description("Column alignment: left, right, center")] string alignment = "left")
    {
        try
        {
            var lines = csv.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
            if (lines.Count < 1) return "Empty CSV.";
            var rows = lines.Select(l => ParseCsvLine(l, ',')).ToList();
            var widths = Enumerable.Range(0, rows[0].Count).Select(i => rows.Max(r => i < r.Count ? r[i].Length : 0)).ToList();
            var sep = alignment.ToLower() switch
            {
                "right"  => widths.Select(w => new string('-', w+1) + ":"),
                "center" => widths.Select(w => ":" + new string('-', w) + ":"),
                _        => widths.Select(w => new string('-', w+2))
            };
            var sb = new StringBuilder();
            sb.AppendLine("| " + string.Join(" | ", rows[0].Select((c, i) => c.PadRight(widths[i]))) + " |");
            sb.AppendLine("|" + string.Join("|", sep.Select(s => s)) + "|");
            foreach (var row in rows.Skip(1))
                sb.AppendLine("| " + string.Join(" | ", widths.Select((w, i) => (i < row.Count ? row[i] : "").PadRight(w))) + " |");
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    // ── Data generation ────────────────────────────────────────

    [KernelFunction, Description("Generate sample/mock data records in JSON format")]
    public string GenerateMockData(
        [Description("Schema as JSON, e.g. {\"name\":\"string\",\"age\":\"int\",\"email\":\"email\",\"date\":\"date\",\"bool\":\"bool\"}")] string schema,
        [Description("Number of records to generate (1-100)")] int count = 5)
    {
        count = Math.Clamp(count, 1, 100);
        var fields = JsonSerializer.Deserialize<Dictionary<string, string>>(schema) ?? [];
        var names = new[] {"Alice","Bob","Carol","David","Eve","Frank","Grace","Hank","Iris","Jack"};
        var domains = new[] {"gmail.com","yahoo.com","example.com","company.org"};
        var records = Enumerable.Range(0, count).Select(i =>
        {
            var row = new Dictionary<string, object?>();
            foreach (var (k, v) in fields)
            {
                row[k] = v.ToLower() switch
                {
                    "string" or "text" => $"{k}_{i+1}",
                    "name"     => names[i % names.Length],
                    "email"    => $"{names[i % names.Length].ToLower()}{i}@{domains[i % domains.Length]}",
                    "int" or "number" or "integer" => i + 1,
                    "float" or "double"  => Math.Round(Random.Shared.NextDouble() * 1000, 2),
                    "bool" or "boolean"  => i % 2 == 0,
                    "date"     => DateTime.Today.AddDays(-i).ToString("yyyy-MM-dd"),
                    "datetime" => DateTime.UtcNow.AddHours(-i).ToString("o"),
                    "uuid" or "guid"     => Guid.NewGuid().ToString(),
                    "phone"    => $"+1-555-{(1000+i):D4}",
                    "url"      => $"https://example.com/{k}/{i+1}",
                    _          => null
                };
            }
            return row;
        }).ToList();
        return JsonSerializer.Serialize(records, _prettyJson);
    }

    // ── Private helpers ────────────────────────────────────────
    private static void AnalyseElement(JsonElement el, string path, StringBuilder sb, int depth)
    {
        var indent = new string(' ', depth * 2);
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                sb.AppendLine($"{indent}{(string.IsNullOrEmpty(path) ? "root" : path)} [object, {el.EnumerateObject().Count()} keys]");
                foreach (var prop in el.EnumerateObject())
                    AnalyseElement(prop.Value, (string.IsNullOrEmpty(path) ? "" : path + ".") + prop.Name, sb, depth + 1);
                break;
            case JsonValueKind.Array:
                sb.AppendLine($"{indent}{path} [array, {el.GetArrayLength()} items]");
                if (el.GetArrayLength() > 0) AnalyseElement(el[0], path + "[0]", sb, depth + 1);
                break;
            default:
                sb.AppendLine($"{indent}{path}: {el.ValueKind} = {el}");
                break;
        }
    }

    private static List<string> ParseCsvLine(string line, char delimiter)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuote = false;
        foreach (char c in line)
        {
            if (c == '"') inQuote = !inQuote;
            else if (c == delimiter && !inQuote) { result.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        result.Add(sb.ToString());
        return result;
    }

    private static string CsvEscape(string? s)
    {
        s ??= "";
        return s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
    }

    private static object XmlNodeToDict(XElement el)
    {
        if (!el.HasElements && !el.HasAttributes) return (object)el.Value;
        var dict = new Dictionary<string, object?>();
        foreach (var attr in el.Attributes()) dict["@" + attr.Name.LocalName] = attr.Value;
        foreach (var child in el.Elements())
        {
            var key = child.Name.LocalName;
            var val = XmlNodeToDict(child);
            if (dict.ContainsKey(key))
            {
                if (dict[key] is List<object> list) list.Add(val);
                else dict[key] = new List<object> { dict[key]!, val };
            }
            else dict[key] = val;
        }
        if (!el.HasElements && el.HasAttributes) dict["#text"] = el.Value;
        return dict;
    }

    private static XElement DictToXmlElement(string name, JsonElement val)
    {
        return val.ValueKind switch
        {
            JsonValueKind.Object  => new XElement(name, val.EnumerateObject().Select(p => DictToXmlElement(p.Name, p.Value))),
            JsonValueKind.Array   => new XElement(name, val.EnumerateArray().Select((v, i) => DictToXmlElement("item", v))),
            _                     => new XElement(name, val.ToString())
        };
    }
}
