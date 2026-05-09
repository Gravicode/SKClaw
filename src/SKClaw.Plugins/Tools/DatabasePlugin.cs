using System.ComponentModel;
using System.Data;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.SemanticKernel;

namespace SKClaw.Plugins.Tools;

/// <summary>
/// DatabasePlugin — SQLite embedded database operations, schema inspection,
/// query execution, data import/export, and AI-powered query generation.
/// For production use, add PostgreSQL/SQL Server connection string support.
/// </summary>
public class DatabasePlugin
{
    private readonly string _defaultConnectionString;
    private readonly Kernel _kernel;

    public DatabasePlugin(Kernel kernel, string connectionString = "Data Source=skclaw_data.db")
    {
        _kernel = kernel;
        _defaultConnectionString = connectionString;
    }

    // ── Schema ─────────────────────────────────────────────────

    [KernelFunction, Description("List all tables and views in the SQLite database")]
    public async Task<string> ListTablesAsync(
        [Description("SQLite connection string or empty to use default")] string connectionString = "")
    {
        var cs = string.IsNullOrEmpty(connectionString) ? _defaultConnectionString : connectionString;
        return await QueryAsync(cs,
            "SELECT type, name, sql FROM sqlite_master WHERE type IN ('table','view') ORDER BY type, name",
            maxRows: 100);
    }

    [KernelFunction, Description("Get the schema (columns, types, constraints) of a table")]
    public async Task<string> GetTableSchemaAsync(
        [Description("Table name")] string tableName,
        [Description("SQLite connection string or empty for default")] string connectionString = "")
    {
        var cs = string.IsNullOrEmpty(connectionString) ? _defaultConnectionString : connectionString;
        var sb = new StringBuilder();

        await using var conn = new SqliteConnection(cs);
        await conn.OpenAsync();

        // PRAGMA table_info
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName})";
        await using var reader = await cmd.ExecuteReaderAsync();

        sb.AppendLine($"Schema for table: {tableName}");
        sb.AppendLine("".PadRight(60, '-'));
        sb.AppendLine($"{"#",-4} {"Column",-20} {"Type",-15} {"NotNull",-8} {"Default",-15} {"PK",-4}");
        sb.AppendLine("".PadRight(60, '-'));
        int count = 0;
        while (await reader.ReadAsync())
        {
            sb.AppendLine($"{reader["cid"],-4} {reader["name"],-20} {reader["type"],-15} {reader["notnull"],-8} {reader["dflt_value"],-15} {reader["pk"],-4}");
            count++;
        }

        // Row count
        await using var cnt = conn.CreateCommand();
        cnt.CommandText = $"SELECT COUNT(*) FROM [{tableName}]";
        var rows = await cnt.ExecuteScalarAsync();
        sb.AppendLine($"\nTotal rows: {rows}  |  Columns: {count}");

        // Indexes
        await using var idx = conn.CreateCommand();
        idx.CommandText = $"PRAGMA index_list({tableName})";
        await using var idxReader = await idx.ExecuteReaderAsync();
        var indexes = new List<string>();
        while (await idxReader.ReadAsync()) indexes.Add($"{idxReader["name"]} (unique={idxReader["unique"]})");
        if (indexes.Count > 0) sb.AppendLine($"Indexes: {string.Join(", ", indexes)}");

        return sb.ToString();
    }

    // ── Query Execution ────────────────────────────────────────

    [KernelFunction, Description("Execute a SELECT query and return results as a formatted table")]
    public async Task<string> SelectAsync(
        [Description("SQL SELECT query")] string sql,
        [Description("SQLite connection string or empty for default")] string connectionString = "",
        [Description("Max rows to return (default 50)")] int maxRows = 50)
    {
        var cs = string.IsNullOrEmpty(connectionString) ? _defaultConnectionString : connectionString;
        return await QueryAsync(cs, sql, maxRows);
    }

    [KernelFunction, Description("Execute a SELECT query and return results as JSON array")]
    public async Task<string> SelectAsJsonAsync(
        [Description("SQL SELECT query")] string sql,
        [Description("SQLite connection string or empty for default")] string connectionString = "",
        [Description("Max rows to return")] int maxRows = 100)
    {
        var cs = string.IsNullOrEmpty(connectionString) ? _defaultConnectionString : connectionString;
        try
        {
            await using var conn = new SqliteConnection(cs);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await using var reader = await cmd.ExecuteReaderAsync();

            var results = new List<Dictionary<string, object?>>();
            int count = 0;
            while (await reader.ReadAsync() && count < maxRows)
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                results.Add(row);
                count++;
            }
            return JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex) { return $"Query error: {ex.Message}"; }
    }

    [KernelFunction, Description("Execute INSERT, UPDATE, DELETE, or DDL statements")]
    public async Task<string> ExecuteAsync(
        [Description("SQL statement (INSERT/UPDATE/DELETE/CREATE/DROP/ALTER)")] string sql,
        [Description("SQLite connection string or empty for default")] string connectionString = "")
    {
        var cs = string.IsNullOrEmpty(connectionString) ? _defaultConnectionString : connectionString;
        try
        {
            await using var conn = new SqliteConnection(cs);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            var rows = await cmd.ExecuteNonQueryAsync();
            return $"✅ Executed. Rows affected: {rows}";
        }
        catch (Exception ex) { return $"Execution error: {ex.Message}"; }
    }

    [KernelFunction, Description("Execute multiple SQL statements as a transaction (all-or-nothing)")]
    public async Task<string> ExecuteTransactionAsync(
        [Description("SQL statements separated by semicolons")] string sql,
        [Description("SQLite connection string or empty for default")] string connectionString = "")
    {
        var cs = string.IsNullOrEmpty(connectionString) ? _defaultConnectionString : connectionString;
        var statements = sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Where(s => s.Length > 0).ToList();
        if (statements.Count == 0) return "No statements to execute.";

        await using var conn = new SqliteConnection(cs);
        await conn.OpenAsync();
        await using var txn = await conn.BeginTransactionAsync();
        try
        {
            int total = 0;
            foreach (var stmt in statements)
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = stmt;
                cmd.Transaction = (SqliteTransaction)txn;
                total += await cmd.ExecuteNonQueryAsync();
            }
            await txn.CommitAsync();
            return $"✅ Transaction committed. {statements.Count} statements, {total} rows affected.";
        }
        catch (Exception ex)
        {
            await txn.RollbackAsync();
            return $"❌ Transaction rolled back: {ex.Message}";
        }
    }

    // ── AI-Powered SQL ─────────────────────────────────────────

    [KernelFunction, Description("Generate and execute a SQL query from a natural language description (Text-to-SQL)")]
    public async Task<string> AskDatabaseAsync(
        [Description("Natural language question about your data, e.g. 'Show me the top 5 customers by revenue'")] string question,
        [Description("Table name(s) to query (leave empty to auto-discover)")] string tables = "",
        [Description("SQLite connection string or empty for default")] string connectionString = "")
    {
        var cs = string.IsNullOrEmpty(connectionString) ? _defaultConnectionString : connectionString;

        // Get schema context
        string schema;
        if (!string.IsNullOrEmpty(tables))
        {
            var parts = tables.Split(',').Select(t => t.Trim());
            var schemas = new List<string>();
            foreach (var t in parts) schemas.Add(await GetTableSchemaAsync(t, cs));
            schema = string.Join("\n\n", schemas);
        }
        else
        {
            schema = await ListTablesAsync(cs);
        }

        // Generate SQL via LLM
        var prompt = $"""
            You are a SQLite expert. Generate a single SELECT SQL query that answers this question.
            Return ONLY the SQL query, no explanation, no markdown code blocks.

            Database Schema:
            {schema}

            Question: {question}
            """;

        var result = await _kernel.InvokePromptAsync(prompt);
        var generatedSql = result.GetValue<string>()?.Trim().TrimStart('`').TrimEnd('`') ?? "";
        // Strip ```sql ``` if present
        if (generatedSql.StartsWith("sql", StringComparison.OrdinalIgnoreCase))
            generatedSql = generatedSql[3..].Trim();

        var sb = new StringBuilder();
        sb.AppendLine($"Generated SQL:\n{generatedSql}\n");
        sb.AppendLine("Results:");
        sb.AppendLine(await QueryAsync(cs, generatedSql, 50));
        return sb.ToString();
    }

    // ── Data Import / Export ───────────────────────────────────

    [KernelFunction, Description("Import CSV data into a SQLite table (creates table if not exists)")]
    public async Task<string> ImportCsvAsync(
        [Description("CSV file path (relative to workspace)")] string csvPath,
        [Description("Target table name")] string tableName,
        [Description("SQLite connection string or empty for default")] string connectionString = "",
        [Description("Drop and recreate table if exists?")] bool overwrite = false)
    {
        var workspace = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".skclaw", "workspace");
        var fullPath = Path.Combine(workspace, csvPath);
        if (!File.Exists(fullPath)) return $"File not found: {csvPath}";

        var lines = await File.ReadAllLinesAsync(fullPath);
        if (lines.Length < 2) return "CSV must have at least a header row and one data row.";

        var headers = lines[0].Split(',').Select(h => h.Trim('"', ' ').Replace(' ', '_')).ToList();
        var cs = string.IsNullOrEmpty(connectionString) ? _defaultConnectionString : connectionString;

        await using var conn = new SqliteConnection(cs);
        await conn.OpenAsync();

        if (overwrite)
            await ExecuteSqlAsync(conn, $"DROP TABLE IF EXISTS [{tableName}]");

        var cols = string.Join(", ", headers.Select(h => $"[{h}] TEXT"));
        await ExecuteSqlAsync(conn, $"CREATE TABLE IF NOT EXISTS [{tableName}] ({cols})");

        int imported = 0;
        await using var txn = await conn.BeginTransactionAsync();
        foreach (var line in lines.Skip(1).Where(l => l.Trim().Length > 0))
        {
            var values = line.Split(',').Select(v => v.Trim('"', ' ')).ToList();
            var placeholders = string.Join(", ", Enumerable.Range(0, headers.Count).Select(i => $"@p{i}"));
            var insertCmd = conn.CreateCommand();
            insertCmd.CommandText = $"INSERT INTO [{tableName}] ({string.Join(", ", headers.Select(h => $"[{h}]"))}) VALUES ({placeholders})";
            insertCmd.Transaction = (SqliteTransaction)txn;
            for (int i = 0; i < headers.Count; i++)
                insertCmd.Parameters.AddWithValue($"@p{i}", i < values.Count ? values[i] : "");
            await insertCmd.ExecuteNonQueryAsync();
            imported++;
        }
        await txn.CommitAsync();

        return $"✅ Imported {imported} rows into table '{tableName}' from '{csvPath}'";
    }

    [KernelFunction, Description("Export a table or query result to CSV file")]
    public async Task<string> ExportCsvAsync(
        [Description("SQL SELECT query or just a table name")] string sqlOrTable,
        [Description("Output CSV filename")] string outputFile,
        [Description("SQLite connection string or empty for default")] string connectionString = "")
    {
        var cs = string.IsNullOrEmpty(connectionString) ? _defaultConnectionString : connectionString;
        var sql = sqlOrTable.ToLower().TrimStart().StartsWith("select") ? sqlOrTable : $"SELECT * FROM [{sqlOrTable}]";

        await using var conn = new SqliteConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync();

        var workspace = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".skclaw", "workspace");
        var path = Path.Combine(workspace, outputFile);
        await using var writer = new StreamWriter(path);

        // Header
        var headers = Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetName(i)).ToList();
        await writer.WriteLineAsync(string.Join(",", headers.Select(h => $"\"{h}\"")));

        int rows = 0;
        while (await reader.ReadAsync())
        {
            var vals = Enumerable.Range(0, reader.FieldCount)
                .Select(i => reader.IsDBNull(i) ? "" : $"\"{reader.GetValue(i).ToString()?.Replace("\"", "\"\"") ?? ""}\"");
            await writer.WriteLineAsync(string.Join(",", vals));
            rows++;
        }

        return $"✅ Exported {rows} rows to '{outputFile}'";
    }

    [KernelFunction, Description("Get quick statistics for a numeric column in a table")]
    public async Task<string> ColumnStatsAsync(
        [Description("Table name")] string tableName,
        [Description("Column name")] string columnName,
        [Description("SQLite connection string or empty for default")] string connectionString = "")
    {
        var cs = string.IsNullOrEmpty(connectionString) ? _defaultConnectionString : connectionString;
        var sql = $"""
            SELECT
              COUNT(*) as count,
              COUNT(DISTINCT [{columnName}]) as unique_count,
              MIN([{columnName}]) as min_val,
              MAX([{columnName}]) as max_val,
              AVG(CAST([{columnName}] AS REAL)) as mean,
              SUM(CAST([{columnName}] AS REAL)) as total,
              COUNT(*) - COUNT([{columnName}]) as null_count
            FROM [{tableName}]
            """;
        return await QueryAsync(cs, sql, 10);
    }

    [KernelFunction, Description("Create a backup of the SQLite database")]
    public async Task<string> BackupDatabaseAsync(
        [Description("SQLite connection string or empty for default")] string connectionString = "",
        [Description("Backup filename (empty = auto-generate)")] string backupFilename = "")
    {
        var cs = string.IsNullOrEmpty(connectionString) ? _defaultConnectionString : connectionString;
        var srcFile = cs.Replace("Data Source=", "").Split(';')[0].Trim();
        if (!File.Exists(srcFile)) return $"Database file not found: {srcFile}";

        var name = string.IsNullOrEmpty(backupFilename)
            ? $"backup_{Path.GetFileNameWithoutExtension(srcFile)}_{DateTime.Now:yyyyMMddHHmmss}.db"
            : backupFilename;
        var dest = Path.Combine(Path.GetDirectoryName(srcFile)!, name);
        File.Copy(srcFile, dest, true);
        var size = new FileInfo(dest).Length;
        return $"✅ Backup created: {dest} ({size / 1024:N0} KB)";
    }

    // ── Helpers ────────────────────────────────────────────────
    private static async Task<string> QueryAsync(string cs, string sql, int maxRows)
    {
        try
        {
            await using var conn = new SqliteConnection(cs);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await using var reader = await cmd.ExecuteReaderAsync();

            var cols = Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetName(i)).ToList();
            var rows = new List<List<string>>();
            while (await reader.ReadAsync() && rows.Count < maxRows)
                rows.Add(Enumerable.Range(0, reader.FieldCount)
                    .Select(i => reader.IsDBNull(i) ? "NULL" : reader.GetValue(i).ToString() ?? "")
                    .ToList());

            if (rows.Count == 0) return "(no rows returned)";

            var widths = cols.Select((c, i) => Math.Max(c.Length, rows.Select(r => r[i].Length).DefaultIfEmpty(0).Max())).ToList();
            var sb = new StringBuilder();
            sb.AppendLine("| " + string.Join(" | ", cols.Select((c, i) => c.PadRight(widths[i]))) + " |");
            sb.AppendLine("|" + string.Join("|", widths.Select(w => new string('-', w + 2))) + "|");
            foreach (var row in rows)
                sb.AppendLine("| " + string.Join(" | ", row.Select((v, i) => v.PadRight(widths[i]))) + " |");
            if (rows.Count == maxRows) sb.AppendLine($"...[showing first {maxRows} rows]");
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex) { return $"Query error: {ex.Message}"; }
    }

    private static async Task ExecuteSqlAsync(SqliteConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
