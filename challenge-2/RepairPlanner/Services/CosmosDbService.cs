using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using RepairPlanner.Models;

namespace RepairPlanner.Services;

/// <summary>
/// Encapsulates all Cosmos DB reads and writes for the RepairPlannerAgent.
/// </summary>
public sealed class CosmosDbService
{
    private readonly CosmosClient _client;
    private readonly CosmosDbOptions _options;
    private readonly ILogger<CosmosDbService> _logger;

    // Primary constructor — parameters become implicit fields.
    // (Like Python's __init__ that assigns self.x = x for each parameter.)
    public CosmosDbService(CosmosDbOptions options, ILogger<CosmosDbService> logger)
    {
        _options = options;
        _logger = logger;

        // CosmosClientOptions sets the serializer to use camelCase JSON, which
        // matches the property names used in our model attributes.
        _client = new CosmosClient(options.Endpoint, options.Key, new CosmosClientOptions
        {
            SerializerOptions = new CosmosSerializationOptions
            {
                PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
            }
        });
    }

    // -------------------------------------------------------------------------
    // Technicians
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns all available technicians whose skill list contains at least one
    /// of the requested skills.
    /// </summary>
    public async Task<List<Technician>> GetAvailableTechniciansBySkillsAsync(
        IReadOnlyList<string> requiredSkills,
        CancellationToken ct = default)
    {
        var container = _client.GetContainer(_options.DatabaseName, _options.TechniciansContainer);

        // Build an IN-list string for the ARRAY_CONTAINS check.
        // Cosmos DB SQL does not support parameterised arrays, so we embed the
        // quoted skill names directly. Values come from our own hardcoded mapping,
        // so there is no injection risk.
        var skillFilters = string.Join(" OR ", requiredSkills
            .Select(s => $"ARRAY_CONTAINS(t.skills, '{s}')"));

        var sql = $"""
            SELECT * FROM t
            WHERE t.available = true
            AND ({skillFilters})
            """;

        _logger.LogDebug("Querying technicians with SQL: {Sql}", sql);

        var results = new List<Technician>();
        try
        {
            var iterator = container.GetItemQueryIterator<Technician>(
                new QueryDefinition(sql));

            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(ct);
                results.AddRange(page);
            }

            _logger.LogInformation("Found {Count} available technician(s) matching skills.", results.Count);
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Cosmos DB error querying technicians. StatusCode={Status}", ex.StatusCode);
            // Return empty list — RepairPlannerAgent will assign null and carry on.
        }

        return results;
    }

    // -------------------------------------------------------------------------
    // Parts inventory
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fetches part documents whose partNumber field matches one of the supplied
    /// numbers. Returns only parts that have stock available.
    /// </summary>
    public async Task<List<Part>> GetPartsByNumbersAsync(
        IReadOnlyList<string> partNumbers,
        CancellationToken ct = default)
    {
        if (partNumbers.Count == 0)
            return [];

        var container = _client.GetContainer(_options.DatabaseName, _options.PartsContainer);

        // Embed quoted part number literals. Values come from our own hardcoded
        // FaultMappingService dictionary, so there is no injection risk.
        var inList = string.Join(", ", partNumbers.Select(p => $"'{p}'"));
        var sql = $"""
            SELECT * FROM p
            WHERE p.partNumber IN ({inList})
            AND p.quantityInStock > 0
            """;

        _logger.LogDebug("Querying parts with SQL: {Sql}", sql);

        var results = new List<Part>();
        try
        {
            var iterator = container.GetItemQueryIterator<Part>(
                new QueryDefinition(sql));

            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(ct);
                results.AddRange(page);
            }

            _logger.LogInformation("Found {Count} part(s) in stock.", results.Count);
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Cosmos DB error querying parts. StatusCode={Status}", ex.StatusCode);
        }

        return results;
    }

    // -------------------------------------------------------------------------
    // Work orders
    // -------------------------------------------------------------------------

    /// <summary>
    /// Writes a new WorkOrder document to the WorkOrders container.
    /// Uses UpsertItemAsync so re-running the program with the same id is safe.
    /// </summary>
    public async Task<WorkOrder> SaveWorkOrderAsync(WorkOrder workOrder, CancellationToken ct = default)
    {
        var container = _client.GetContainer(_options.DatabaseName, _options.WorkOrdersContainer);

        // Ensure the Cosmos DB "id" field is populated.
        // ??= means "assign if null" (like Python: wo.id = wo.id or wo.WorkOrderNumber)
        workOrder.Id ??= workOrder.WorkOrderNumber;

        _logger.LogInformation("Saving work order {Id} to Cosmos DB.", workOrder.Id);

        try
        {
            var response = await container.UpsertItemAsync(
                workOrder,
                new PartitionKey(workOrder.Status),
                cancellationToken: ct);

            _logger.LogInformation(
                "Work order {Id} saved. RU charge: {RU}",
                workOrder.Id, response.RequestCharge);

            return response.Resource;
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Cosmos DB error saving work order {Id}. StatusCode={Status}",
                workOrder.Id, ex.StatusCode);
            throw;
        }
    }
}
