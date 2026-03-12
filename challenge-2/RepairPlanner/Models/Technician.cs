using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models;

/// <summary>
/// A technician record read from the Cosmos DB "Technicians" container.
/// Partition key: "department"
/// </summary>
public sealed class Technician
{
    // Cosmos DB requires an "id" field (lowercase) on every document.
    [JsonPropertyName("id")]
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    // Partition key for the Technicians container.
    [JsonPropertyName("department")]
    [JsonProperty("department")]
    public string Department { get; set; } = string.Empty;

    // List of skill identifiers, e.g. ["tire_curing_press", "temperature_control"]
    [JsonPropertyName("skills")]
    [JsonProperty("skills")]
    public List<string> Skills { get; set; } = new();

    // Whether the technician is currently available for assignment.
    [JsonPropertyName("available")]
    [JsonProperty("available")]
    public bool Available { get; set; } = true;

    [JsonPropertyName("certifications")]
    [JsonProperty("certifications")]
    public List<string> Certifications { get; set; } = new();

    [JsonPropertyName("contactInfo")]
    [JsonProperty("contactInfo")]
    public string ContactInfo { get; set; } = string.Empty;
}
