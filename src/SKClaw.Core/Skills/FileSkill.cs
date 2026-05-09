using System.ComponentModel;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.SemanticKernel;

namespace SKClaw.Core.Skills;

/// <summary>
/// FileSkill — Comprehensive file and directory operations.
/// Sandboxed to a workspace directory for safety.
/// Operations: CRUD, search, metadata, zip/unzip, diff, CSV, hash.
/// </summary>
public class FileSkill
{
    private readonly string _workspace;

    public FileSkill(string? workspaceRoot = null)
    {
        _workspace = workspaceRoot
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".skclaw", "workspace");
        Directory.CreateDirectory(_workspace);
    }

    // ── Read ───────────────────────────────────────────────────

    [KernelFunction, Description("Read the full content of a file from the workspace")]
    public async Task<string> ReadFileAsync(
        [Description("File path relative to workspace")] string path,
        [Description("Encoding: utf8, ascii, latin1")] string encoding = "utf8")
    {
        var full = SafePath(path);
        if (!File.Exists(full)) return $"File not found: {path}";
        try
        {
            var enc = GetEncoding(encoding);
            var text = await File.ReadAllTextAsync(full, enc);
            return text.Length > 16000 ? text[..16000] + $"\n...[truncated {text.Length - 16000} chars]" : text;
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    [KernelFunction, Description("Read specific lines from a file (1-based line numbers)")]
    public async Task<string> ReadLinesAsync(
        [Description("File path")] string path,
        [Description("Start line number (1-based)")] int startLine = 1,
        [Description("End line number (0 = to end)")] int endLine = 0)
    {
        var full = SafePath(path);
        if (!File.Exists(full)) return $"File not found: {path}";
        var lines = await File.ReadAllLinesAsync(full);
        int start = Math.Max(1, startLine) - 1;
        int end   = endLine == 0 ? lines.Length : Math.Min(endLine, lines.Length);
        var result = lines.Skip(start).Take(end - start).Select((l, i) => $"{start + i + 1:D4}: {l}");
        return string.Join("\n", result);
    }

    [KernelFunction, Description("Read a binary file and return as base64")]
    public async Task<string> ReadBinaryAsync([Description("File path")] string path)
    {
        var full = SafePath(path);
        if (!File.Exists(full)) return $"File not found: {path}";
        var bytes = await File.ReadAllBytesAsync(full);
        return Convert.ToBase64String(bytes);
    }

    // ── Write ──────────────────────────────────────────────────

    [KernelFunction, Description("Write content to a file, creating parent directories if needed")]
    public async Task<string> WriteFileAsync(
        [Description("File path relative to workspace")] string path,
        [Description("File content")] string content,
        [Description("Encoding: utf8, ascii, latin1")] string encoding = "utf8")
    {
        var full = SafePath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, content, GetEncoding(encoding));
        return $"Written {content.Length} chars to '{path}'.";
    }

    [KernelFunction, Description("Append text to an existing file")]
    public async Task<string> AppendFileAsync(
        [Description("File path")] string path,
        [Description("Text to append")] string content,
        [Description("Add newline before appended content?")] bool addNewline = true)
    {
        var full = SafePath(path);
        if (addNewline && File.Exists(full)) content = "\n" + content;
        await File.AppendAllTextAsync(full, content);
        return $"Appended {content.Length} chars to '{path}'.";
    }

    [KernelFunction, Description("Write a base64 string as a binary file")]
    public async Task<string> WriteBinaryAsync(
        [Description("File path")] string path,
        [Description("Base64-encoded content")] string base64Content)
    {
        var full = SafePath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllBytesAsync(full, Convert.FromBase64String(base64Content));
        return $"Written binary file to '{path}'.";
    }

    [KernelFunction, Description("Insert text at a specific line in a file")]
    public async Task<string> InsertLineAsync(
        [Description("File path")] string path,
        [Description("Line number to insert at (1-based, 0 = end)")] int lineNumber,
        [Description("Text to insert")] string text)
    {
        var full = SafePath(path);
        if (!File.Exists(full)) return $"File not found: {path}";
        var lines = (await File.ReadAllLinesAsync(full)).ToList();
        int idx = lineNumber == 0 ? lines.Count : Math.Clamp(lineNumber - 1, 0, lines.Count);
        lines.Insert(idx, text);
        await File.WriteAllLinesAsync(full, lines);
        return $"Inserted at line {idx + 1} in '{path}'.";
    }

    // ── Delete / Rename / Copy / Move ──────────────────────────

    [KernelFunction, Description("Delete a file from the workspace")]
    public string DeleteFile([Description("File path")] string path)
    {
        var full = SafePath(path);
        if (!File.Exists(full)) return $"File not found: {path}";
        File.Delete(full);
        return $"Deleted '{path}'.";
    }

    [KernelFunction, Description("Copy a file within the workspace")]
    public string CopyFile(
        [Description("Source file path")] string source,
        [Description("Destination file path")] string destination,
        [Description("Overwrite if exists?")] bool overwrite = false)
    {
        var src = SafePath(source);
        var dst = SafePath(destination);
        if (!File.Exists(src)) return $"Source not found: {source}";
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        File.Copy(src, dst, overwrite);
        return $"Copied '{source}' → '{destination}'.";
    }

    [KernelFunction, Description("Move or rename a file within the workspace")]
    public string MoveFile(
        [Description("Source file path")] string source,
        [Description("Destination file path")] string destination)
    {
        var src = SafePath(source);
        var dst = SafePath(destination);
        if (!File.Exists(src)) return $"Source not found: {source}";
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        File.Move(src, dst);
        return $"Moved '{source}' → '{destination}'.";
    }

    // ── Directory ──────────────────────────────────────────────

    [KernelFunction, Description("List files and folders in the workspace directory")]
    public string ListDirectory(
        [Description("Subdirectory path (empty = root workspace)")] string path = "",
        [Description("Include subdirectories recursively?")] bool recursive = false,
        [Description("Filter by extension, e.g. .txt or *.cs (empty = all)")] string filter = "")
    {
        var dir = string.IsNullOrEmpty(path) ? _workspace : SafePath(path);
        if (!Directory.Exists(dir)) return $"Directory not found: {path}";

        var opt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var searchPattern = string.IsNullOrEmpty(filter) ? "*" : filter;

        var files = Directory.GetFiles(dir, searchPattern, opt)
            .Select(f => new FileInfo(f))
            .OrderBy(fi => fi.FullName)
            .Select(fi => $"  📄 {fi.FullName[(dir.Length + 1)..].Replace('\\', '/')} ({FormatSize(fi.Length)}, {fi.LastWriteTime:yyyy-MM-dd HH:mm})");

        var dirs = Directory.GetDirectories(dir, "*", opt)
            .Select(d => $"  📁 {d[(dir.Length + 1)..].Replace('\\', '/')}/")
            .OrderBy(d => d);

        var all = dirs.Concat(files).ToList();
        return all.Count == 0
            ? "Directory is empty."
            : $"Contents of /{path}:\n" + string.Join("\n", all);
    }

    [KernelFunction, Description("Create one or more directories in the workspace")]
    public string CreateDirectory([Description("Directory path")] string path)
    {
        Directory.CreateDirectory(SafePath(path));
        return $"Directory created: '{path}'";
    }

    [KernelFunction, Description("Delete a directory and all its contents")]
    public string DeleteDirectory(
        [Description("Directory path")] string path,
        [Description("Must be true to confirm deletion of non-empty directory")] bool confirmDelete = false)
    {
        var full = SafePath(path);
        if (!Directory.Exists(full)) return $"Directory not found: {path}";
        var count = Directory.GetFiles(full, "*", SearchOption.AllDirectories).Length;
        if (count > 0 && !confirmDelete)
            return $"Directory contains {count} files. Set confirmDelete=true to proceed.";
        Directory.Delete(full, true);
        return $"Deleted directory '{path}' ({count} files removed).";
    }

    // ── Search ─────────────────────────────────────────────────

    [KernelFunction, Description("Search for files by name pattern in the workspace")]
    public string FindFiles(
        [Description("Search pattern, e.g. *.txt, report*, *2024*")] string pattern,
        [Description("Start from subdirectory (empty = root)")] string directory = "",
        [Description("Include subdirectories?")] bool recursive = true)
    {
        var dir = string.IsNullOrEmpty(directory) ? _workspace : SafePath(directory);
        var opt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.GetFiles(dir, pattern, opt)
            .Select(f => $"  {f[(dir.Length + 1)..].Replace('\\', '/')} ({FormatSize(new FileInfo(f).Length)})")
            .ToList();
        return files.Count == 0 ? "No files found." : $"Found {files.Count} file(s):\n" + string.Join("\n", files);
    }

    [KernelFunction, Description("Search for text content within files")]
    public async Task<string> GrepAsync(
        [Description("Text or regex pattern to search for")] string pattern,
        [Description("File pattern to search in, e.g. *.txt")] string filePattern = "*",
        [Description("Case sensitive?")] bool caseSensitive = false,
        [Description("Max results to return")] int maxResults = 50)
    {
        var results = new List<string>();
        var opt = System.Text.RegularExpressions.RegexOptions.None;
        if (!caseSensitive) opt |= System.Text.RegularExpressions.RegexOptions.IgnoreCase;

        foreach (var file in Directory.GetFiles(_workspace, filePattern, SearchOption.AllDirectories))
        {
            if (results.Count >= maxResults) break;
            try
            {
                var lines = await File.ReadAllLinesAsync(file);
                for (int i = 0; i < lines.Length && results.Count < maxResults; i++)
                {
                    if (System.Text.RegularExpressions.Regex.IsMatch(lines[i], pattern, opt))
                        results.Add($"{file[(_workspace.Length + 1)..].Replace('\\', '/')}:{i + 1}: {lines[i].Trim()}");
                }
            }
            catch { /* skip binary/unreadable files */ }
        }
        return results.Count == 0 ? "No matches found." : $"{results.Count} match(es):\n" + string.Join("\n", results);
    }

    // ── Metadata ───────────────────────────────────────────────

    [KernelFunction, Description("Get detailed metadata for a file or directory")]
    public string GetMetadata([Description("File or directory path")] string path)
    {
        var full = SafePath(path);
        if (File.Exists(full))
        {
            var fi = new FileInfo(full);
            var hash = ComputeSha256(full);
            return $"""
                Type        : File
                Name        : {fi.Name}
                Extension   : {fi.Extension}
                Size        : {FormatSize(fi.Length)} ({fi.Length:N0} bytes)
                Created     : {fi.CreationTime:yyyy-MM-dd HH:mm:ss}
                Modified    : {fi.LastWriteTime:yyyy-MM-dd HH:mm:ss}
                Accessed    : {fi.LastAccessTime:yyyy-MM-dd HH:mm:ss}
                ReadOnly    : {fi.IsReadOnly}
                SHA-256     : {hash}
                """;
        }
        if (Directory.Exists(full))
        {
            var di = new DirectoryInfo(full);
            var files = di.GetFiles("*", SearchOption.AllDirectories);
            long totalSize = files.Sum(f => f.Length);
            return $"""
                Type        : Directory
                Name        : {di.Name}
                Files       : {files.Length}
                Total size  : {FormatSize(totalSize)}
                Created     : {di.CreationTime:yyyy-MM-dd HH:mm:ss}
                Modified    : {di.LastWriteTime:yyyy-MM-dd HH:mm:ss}
                """;
        }
        return $"Path not found: {path}";
    }

    // ── Zip ────────────────────────────────────────────────────

    [KernelFunction, Description("Create a ZIP archive from files or a directory")]
    public async Task<string> ZipAsync(
        [Description("Source path (file or directory)")] string sourcePath,
        [Description("Output zip file name")] string zipName)
    {
        var src = SafePath(sourcePath);
        var dst = SafePath(zipName.EndsWith(".zip") ? zipName : zipName + ".zip");
        if (File.Exists(src))
        {
            using var zip = ZipFile.Open(dst, ZipArchiveMode.Create);
            zip.CreateEntryFromFile(src, Path.GetFileName(src));
        }
        else if (Directory.Exists(src))
        {
            ZipFile.CreateFromDirectory(src, dst, CompressionLevel.Optimal, true);
        }
        else return $"Source not found: {sourcePath}";

        var size = new FileInfo(dst).Length;
        return $"Created '{zipName}' ({FormatSize(size)})";
    }

    [KernelFunction, Description("Extract a ZIP archive to a directory")]
    public string Unzip(
        [Description("Path to the .zip file")] string zipPath,
        [Description("Destination directory")] string destDir,
        [Description("Overwrite existing files?")] bool overwrite = false)
    {
        var src = SafePath(zipPath);
        var dst = SafePath(destDir);
        if (!File.Exists(src)) return $"Zip file not found: {zipPath}";
        Directory.CreateDirectory(dst);
        ZipFile.ExtractToDirectory(src, dst, overwrite);
        return $"Extracted '{zipPath}' to '{destDir}'.";
    }

    // ── Text Utilities ─────────────────────────────────────────

    [KernelFunction, Description("Compare two text files and return unified diff")]
    public async Task<string> DiffFilesAsync(
        [Description("First file path")] string file1,
        [Description("Second file path")] string file2)
    {
        var f1 = SafePath(file1);
        var f2 = SafePath(file2);
        if (!File.Exists(f1)) return $"Not found: {file1}";
        if (!File.Exists(f2)) return $"Not found: {file2}";

        var lines1 = await File.ReadAllLinesAsync(f1);
        var lines2 = await File.ReadAllLinesAsync(f2);
        var sb = new StringBuilder();
        sb.AppendLine($"--- {file1}");
        sb.AppendLine($"+++ {file2}");

        var maxLen = Math.Max(lines1.Length, lines2.Length);
        int changes = 0;
        for (int i = 0; i < maxLen; i++)
        {
            var a = i < lines1.Length ? lines1[i] : null;
            var b = i < lines2.Length ? lines2[i] : null;
            if (a != b)
            {
                changes++;
                if (a != null) sb.AppendLine($"@L{i+1}  - {a}");
                if (b != null) sb.AppendLine($"@L{i+1}  + {b}");
            }
        }
        return changes == 0 ? "Files are identical." : sb.ToString();
    }

    [KernelFunction, Description("Read and parse CSV file, returning it as a formatted table")]
    public async Task<string> ReadCsvAsync(
        [Description("CSV file path")] string path,
        [Description("Show first N rows (0 = all)")] int maxRows = 20)
    {
        var full = SafePath(path);
        if (!File.Exists(full)) return $"File not found: {path}";
        var lines = await File.ReadAllLinesAsync(full);
        if (lines.Length == 0) return "Empty CSV file.";

        var rows = lines.Select(l => l.Split(',').Select(c => c.Trim('"', ' ')).ToArray()).ToList();
        var cols = rows.Select(r => r.Length).Max();
        var widths = Enumerable.Range(0, cols).Select(i => rows.Select(r => i < r.Length ? r[i].Length : 0).Max()).ToArray();

        var sb = new StringBuilder();
        var display = maxRows > 0 ? rows.Take(maxRows + 1).ToList() : rows;
        foreach (var (row, idx) in display.Select((r, i) => (r, i)))
        {
            var cells = Enumerable.Range(0, cols).Select(i => (i < row.Length ? row[i] : "").PadRight(widths[i]));
            sb.AppendLine("| " + string.Join(" | ", cells) + " |");
            if (idx == 0) sb.AppendLine("|" + string.Join("|", widths.Select(w => new string('-', w + 2))) + "|");
        }
        if (maxRows > 0 && rows.Count > maxRows + 1)
            sb.AppendLine($"...and {rows.Count - maxRows - 1} more rows");
        return $"CSV: {rows.Count - 1} data rows, {cols} columns\n\n" + sb;
    }

    // ── Private helpers ────────────────────────────────────────
    private string SafePath(string relative)
    {
        var full = Path.GetFullPath(Path.Combine(_workspace, relative));
        if (!full.StartsWith(_workspace))
            throw new UnauthorizedAccessException("Access outside workspace is not allowed.");
        return full;
    }

    private static Encoding GetEncoding(string name) => name.ToLower() switch
    {
        "ascii" => Encoding.ASCII,
        "latin1" or "iso-8859-1" => Encoding.Latin1,
        _ => Encoding.UTF8
    };

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };

    private static string ComputeSha256(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLower();
    }
}
