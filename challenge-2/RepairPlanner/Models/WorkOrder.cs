using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models;

/// <summary>
/// The output produced by the RepairPlannerAgent and saved to the Cosmos DB
/// "WorkOrders" container. Partition key: "status"
/// </summary>
public sealed class WorkOrder
{
    // Cosmos DB document id. Set to workOrderNumber if not provided.
    [JsonPropertyName("id")]
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    // Human-readable identifier, e.g. "WO-machine-001-20260312-1"
    [JsonPropertyName("workOrderNumber")]
    [JsonProperty("workOrderNumber")]
    public string WorkOrderNumber { get; set; } = string.Empty;

    [JsonPropertyName("machineId")]
    [JsonProperty("machineId")]
    public string MachineId { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    // "corrective" | "preventive" | "emergency"
    [JsonPropertyName("type")]
    [JsonProperty("type")]
    public string Type { get; set; } = "corrective";

    // "critical" | "high" | "medium" | "low"
    // ??= means "assign if null" (like Python: self.priority = self.priority or "medium")
    [JsonPropertyName("priority")]
    [JsonProperty("priority")]
    public string? Priority { get; set; }

    // Partition key. Typically "open" when first created.
    [JsonPropertyName("status")]
    [JsonProperty("status")]
    public string Status { get; set; } = "open";

    // Technician id (or null if none could be assigned).
    // ?? means "if null, use this" (like Python: technician_id or None)
    [JsonPropertyName("assignedTo")]
    [JsonProperty("assignedTo")]
    public string? AssignedTo { get; set; }

    [JsonPropertyName("notes")]
    [JsonProperty("notes")]
    public string Notes { get; set; } = string.Empty;

    // Total estimated duration in minutes (integer — not a string).
    [JsonPropertyName("estimatedDuration")]
    [JsonProperty("estimatedDuration")]
    public int EstimatedDuration { get; set; }

    // Parts required to complete all tasks.
    [JsonPropertyName("partsUsed")]
    [JsonProperty("partsUsed")]
    public List<WorkOrderPartUsage> PartsUsed { get; set; } = new();

    // Ordered list of repair steps.
    [JsonPropertyName("tasks")]
    [JsonProperty("tasks")]
    public List<RepairTask> Tasks { get; set; } = new();

    // ISO 8601 timestamp when this record was created.
    [JsonPropertyName("createdAt")]
    [JsonProperty("createdAt")]
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("o");

    // Copy of the originating fault for traceability.
    [JsonPropertyName("diagnosedFault")]
    [JsonProperty("diagnosedFault")]
    public DiagnosedFault? DiagnosedFault { get; set; }
}
