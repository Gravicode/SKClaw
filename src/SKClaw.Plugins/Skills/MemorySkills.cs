using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace SKClaw.Plugins.Skills;

/// <summary>
/// Memory skill inspired by LanceDB and memU.
/// Provides long-term knowledge storage and recall.
/// </summary>
public class MemorySkills
{
    // Simulation of a vector database storage
    private static readonly Dictionary<string, string> _longTermMemory = new();

    [KernelFunction, Description("Store information in long-term memory (LanceDB style)")]
    public string StoreInformation(
        [Description("Key or topic to store")] string key,
        [Description("The actual information or fact to remember")] string content)
    {
        _longTermMemory[key.ToLower()] = content;
        return $"💾 Information stored under key: '{key}'. Vector indexing complete.";
    }

    [KernelFunction, Description("Recall information from long-term memory")]
    public string RecallInformation(
        [Description("The topic or keyword to search for")] string query)
    {
        var key = query.ToLower();
        if (_longTermMemory.TryGetValue(key, out var content))
        {
            return $"🧠 Found in memory for '{query}': {content}";
        }

        // Simulating semantic search
        var partial = _longTermMemory.FirstOrDefault(x => x.Key.Contains(key));
        if (partial.Value != null)
        {
            return $"🧠 (Partial match) Found in memory for '{query}': {partial.Value}";
        }

        return "❌ No matching information found in long-term memory.";
    }

    [KernelFunction, Description("Update a knowledge graph relationship (memU style)")]
    public string LinkConcepts(
        [Description("Source concept")] string conceptA,
        [Description("Target concept")] string conceptB,
        [Description("Relationship type (e.g. 'is part of', 'works for', 'related to')")] string relation)
    {
        return $"🕸️ Knowledge Graph Updated: [{conceptA}] --({relation})--> [{conceptB}]";
    }
}
