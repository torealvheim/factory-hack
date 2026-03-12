using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models;

/// <summary>
/// A single ordered step inside a WorkOrder.
/// Nested inside WorkOrder.Tasks — not a standalone Cosmos DB document.
/// </summary>
public sealed class RepairTask
{
    // 1-based execution order within the work order.
    [JsonPropertyName("sequence")]
    [JsonProperty("sequence")]
    public int Sequence { get; set; }

    [JsonPropertyName("title")]
    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    // Duration must be an integer (minutes). LLMs sometimes return strings like
    // "60 minutes" — handled in RepairPlannerAgent with JsonNumberHandling.AllowReadingFromString.
    [JsonPropertyName("estimatedDurationMinutes")]
    [JsonProperty("estimatedDurationMinutes")]
    public int EstimatedDurationMinutes { get; set; }

    // Skills required to perform this task, e.g. ["temperature_control", "instrumentation"]
    [JsonPropertyName("requiredSkills")]
    [JsonProperty("requiredSkills")]
    public List<string> RequiredSkills { get; set; } = new();

    // Safety instructions specific to this task (may be empty string).
    [JsonPropertyName("safetyNotes")]
    [JsonProperty("safetyNotes")]
    public string SafetyNotes { get; set; } = string.Empty;
}
