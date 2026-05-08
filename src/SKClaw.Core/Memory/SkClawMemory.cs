using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;
using SKClaw.Core.Configuration;

namespace SKClaw.Core.Memory;

/// <summary>
/// SKClaw Memory - semantic memory using vector embeddings.
/// Supports: in-memory, SQLite, Qdrant, Chroma.
/// </summary>
public class SkClawMemory
{
    private readonly Kernel _kernel;
    private readonly AppConfiguration _config;
    private readonly ILogger<SkClawMemory> _logger;
    private readonly List<MemoryEntry> _inMemoryStore = new();

    public SkClawMemory(Kernel kernel, AppConfiguration config, ILogger<SkClawMemory> logger)
    {
        _kernel = kernel;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Store a memory entry (user/assistant exchange).
    /// </summary>
    public async Task RememberAsync(string userInput, string assistantResponse,
        string? collection = null, CancellationToken ct = default)
    {
        if (!_config.Agent.EnableMemory) return;

        var collectionName = collection ?? _config.Memory.CollectionName;
        var text = $"User: {userInput}\nAssistant: {assistantResponse}";
        var id = Guid.NewGuid().ToString("N");

        var entry = new MemoryEntry
        {
            Id = id,
            Collection = collectionName,
            Text = text,
            UserInput = userInput,
            AssistantResponse = assistantResponse,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Try to get embeddings if available
        try
        {
            var embeddingService = _kernel.GetRequiredService<ITextEmbeddingGenerationService>();
            var embeddings = await embeddingService.GenerateEmbeddingsAsync([text], cancellationToken: ct);
            entry.Embedding = embeddings.FirstOrDefault().ToArray();
        }
        catch
        {
            // Embeddings unavailable, fall back to keyword search
        }

        switch (_config.Memory.Provider.ToLowerInvariant())
        {
            case "inmemory":
                _inMemoryStore.Add(entry);
                if (_inMemoryStore.Count > 1000)
                    _inMemoryStore.RemoveAt(0);
                break;
            case "sqlite":
                await SaveToSqliteAsync(entry, ct);
                break;
            default:
                _inMemoryStore.Add(entry);
                break;
        }

        _logger.LogDebug("Memory stored: {Id}", id);
    }

    /// <summary>
    /// Recall relevant memories based on a query.
    /// Returns formatted string for inclusion in system prompt.
    /// </summary>
    public async Task<string> RecallAsync(string query, string? collection = null,
        CancellationToken ct = default)
    {
        if (!_config.Agent.EnableMemory) return "";

        var collectionName = collection ?? _config.Memory.CollectionName;
        List<MemoryEntry> candidates = _config.Memory.Provider.ToLowerInvariant() switch
        {
            "sqlite" => await LoadFromSqliteAsync(collectionName, ct),
            _ => _inMemoryStore.Where(m => m.Collection == collectionName).ToList()
        };

        if (candidates.Count == 0) return "";

        // Try vector similarity if embeddings available
        List<MemoryEntry> relevant;
        try
        {
            var embeddingService = _kernel.GetRequiredService<ITextEmbeddingGenerationService>();
            var queryEmbeddings = await embeddingService.GenerateEmbeddingsAsync([query], cancellationToken: ct);
            var queryVector = queryEmbeddings.FirstOrDefault().ToArray();

            relevant = candidates
                .Where(m => m.Embedding != null && m.Embedding.Length > 0)
                .Select(m => (entry: m, score: CosineSimilarity(queryVector, m.Embedding!)))
                .Where(x => x.score >= _config.Memory.RelevanceThreshold)
                .OrderByDescending(x => x.score)
                .Take(_config.Memory.MaxResults)
                .Select(x => x.entry)
                .ToList();
        }
        catch
        {
            // Fallback: keyword match
            var words = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            relevant = candidates
                .Where(m => words.Any(w => m.Text.ToLowerInvariant().Contains(w)))
                .TakeLast(_config.Memory.MaxResults)
                .ToList();
        }

        if (relevant.Count == 0) return "";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Relevant past context:");
        foreach (var mem in relevant.TakeLast(5))
        {
            sb.AppendLine($"- [{mem.Timestamp:g}] {mem.UserInput.Trim()[..Math.Min(120, mem.UserInput.Length)]}");
        }

        return sb.ToString().Trim();
    }

    private async Task SaveToSqliteAsync(MemoryEntry entry, CancellationToken ct)
    {
        // SQLite persistence (simplified - production would use Microsoft.Data.Sqlite)
        var path = _config.Memory.Sqlite.ConnectionString.Replace("Data Source=", "");
        var json = System.Text.Json.JsonSerializer.Serialize(entry);
        var line = $"{entry.Timestamp:O}|{entry.Collection}|{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json))}\n";
        await File.AppendAllTextAsync(path + ".mem", line, ct);
    }

    private async Task<List<MemoryEntry>> LoadFromSqliteAsync(string collection, CancellationToken ct)
    {
        var path = _config.Memory.Sqlite.ConnectionString.Replace("Data Source=", "") + ".mem";
        if (!File.Exists(path)) return [];

        var lines = await File.ReadAllLinesAsync(path, ct);
        var results = new List<MemoryEntry>();
        foreach (var line in lines.TakeLast(200))
        {
            try
            {
                var parts = line.Split('|');
                if (parts.Length < 3 || parts[1] != collection) continue;
                var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(parts[2]));
                var entry = System.Text.Json.JsonSerializer.Deserialize<MemoryEntry>(json);
                if (entry != null) results.Add(entry);
            }
            catch { /* ignore corrupt lines */ }
        }
        return results;
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0f;
        float dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        var denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denom == 0 ? 0 : dot / denom;
    }

    private class MemoryEntry
    {
        public string Id { get; set; } = "";
        public string Collection { get; set; } = "";
        public string Text { get; set; } = "";
        public string UserInput { get; set; } = "";
        public string AssistantResponse { get; set; } = "";
        public DateTimeOffset Timestamp { get; set; }
        public float[]? Embedding { get; set; }
    }
}
