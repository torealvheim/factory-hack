using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models;

/// <summary>
/// A spare part record read from the Cosmos DB "PartsInventory" container.
/// Partition key: "category"
/// </summary>
public sealed class Part
{
    // Cosmos DB document id.
    [JsonPropertyName("id")]
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    // Human-readable part number, e.g. "TCP-HTR-4KW"
    [JsonPropertyName("partNumber")]
    [JsonProperty("partNumber")]
    public string PartNumber { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    // Partition key for the PartsInventory container.
    [JsonPropertyName("category")]
    [JsonProperty("category")]
    public string Category { get; set; } = string.Empty;

    // How many units are available in the warehouse.
    [JsonPropertyName("quantityInStock")]
    [JsonProperty("quantityInStock")]
    public int QuantityInStock { get; set; }

    [JsonPropertyName("unitPrice")]
    [JsonProperty("unitPrice")]
    public decimal UnitPrice { get; set; }

    [JsonPropertyName("supplier")]
    [JsonProperty("supplier")]
    public string Supplier { get; set; } = string.Empty;

    // Lead time in days if the part needs to be ordered.
    [JsonPropertyName("leadTimeDays")]
    [JsonProperty("leadTimeDays")]
    public int LeadTimeDays { get; set; }
}
