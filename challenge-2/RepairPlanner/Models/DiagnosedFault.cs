using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models;

/// <summary>
/// Input received from the Python FaultDiagnosisAgent (Challenge 1).
/// Property names use PascalCase to match the agent's JSON output.
/// </summary>
public sealed class DiagnosedFault
{
    // Both [JsonPropertyName] (System.Text.Json) and [JsonProperty] (Newtonsoft.Json)
    // are applied so the model works with both serializers.

    [JsonPropertyName("MachineId")]
    [JsonProperty("MachineId")]
    public string MachineId { get; set; } = string.Empty;

    [JsonPropertyName("FaultType")]
    [JsonProperty("FaultType")]
    public string FaultType { get; set; } = string.Empty;

    [JsonPropertyName("RootCause")]
    [JsonProperty("RootCause")]
    public string RootCause { get; set; } = string.Empty;

    // "Low" | "Medium" | "High" | "Critical" | "Unknown"
    [JsonPropertyName("Severity")]
    [JsonProperty("Severity")]
    public string Severity { get; set; } = string.Empty;

    // ISO 8601 timestamp, e.g. "2026-01-16T12:34:56Z"
    [JsonPropertyName("DetectedAt")]
    [JsonProperty("DetectedAt")]
    public string DetectedAt { get; set; } = string.Empty;

    // Supporting details from the fault diagnosis (KB articles, thresholds, etc.)
    [JsonPropertyName("Metadata")]
    [JsonProperty("Metadata")]
    public Dictionary<string, object?> Metadata { get; set; } = new();
}
