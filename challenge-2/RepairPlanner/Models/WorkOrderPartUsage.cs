using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models;

/// <summary>
/// Records a specific part and quantity used in a work order.
/// Nested inside WorkOrder.PartsUsed — not a standalone Cosmos DB document.
/// </summary>
public sealed class WorkOrderPartUsage
{
    // References Part.Id in the PartsInventory container.
    [JsonPropertyName("partId")]
    [JsonProperty("partId")]
    public string PartId { get; set; } = string.Empty;

    // Denormalised for readability, e.g. "TCP-HTR-4KW"
    [JsonPropertyName("partNumber")]
    [JsonProperty("partNumber")]
    public string PartNumber { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    [JsonProperty("quantity")]
    public int Quantity { get; set; } = 1;
}
