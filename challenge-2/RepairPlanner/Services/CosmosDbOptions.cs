namespace RepairPlanner.Services;

/// <summary>
/// Typed options bag for Cosmos DB connection details.
/// Populated from environment variables in Program.cs.
/// </summary>
public sealed class CosmosDbOptions
{
    public string Endpoint { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;

    // Container names — must match what was provisioned in Challenge 0.
    public string TechniciansContainer { get; set; } = "Technicians";
    public string PartsContainer { get; set; } = "PartsInventory";
    public string WorkOrdersContainer { get; set; } = "WorkOrders";
}
