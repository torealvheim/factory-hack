// Top-level program — C# runs this file as the entry point automatically.
// No explicit class or Main() method needed (like a Python script at module level).
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RepairPlanner;
using RepairPlanner.Models;
using RepairPlanner.Services;
using System.Text.Json;

// ─── 1. Load environment variables ───────────────────────────────────────────
// Each call throws if the variable is missing rather than silently using null.
var projectEndpoint = Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT")
    ?? throw new InvalidOperationException("AZURE_AI_PROJECT_ENDPOINT is not set.");

var modelDeploymentName = Environment.GetEnvironmentVariable("MODEL_DEPLOYMENT_NAME")
    ?? throw new InvalidOperationException("MODEL_DEPLOYMENT_NAME is not set.");

var cosmosEndpoint = Environment.GetEnvironmentVariable("COSMOS_ENDPOINT")
    ?? throw new InvalidOperationException("COSMOS_ENDPOINT is not set.");

var cosmosKey = Environment.GetEnvironmentVariable("COSMOS_KEY")
    ?? throw new InvalidOperationException("COSMOS_KEY is not set.");

var cosmosDatabaseName = Environment.GetEnvironmentVariable("COSMOS_DATABASE_NAME")
    ?? throw new InvalidOperationException("COSMOS_DATABASE_NAME is not set.");

// ─── 2. Set up logging via Microsoft.Extensions.DependencyInjection ──────────
// `await using` is like Python's `async with`: disposes the scope on exit.
var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
await using var provider = services.BuildServiceProvider();

var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
var programLogger  = loggerFactory.CreateLogger<Program>();

// ─── 3. Instantiate infrastructure services ───────────────────────────────────
var cosmosOptions = new CosmosDbOptions
{
    Endpoint     = cosmosEndpoint,
    Key          = cosmosKey,
    DatabaseName = cosmosDatabaseName
};

var cosmosDb     = new CosmosDbService(cosmosOptions, loggerFactory.CreateLogger<CosmosDbService>());
var faultMapping = new FaultMappingService();

// ─── 4. Build the Azure AI Project client ────────────────────────────────────
// DefaultAzureCredential picks up: az login, managed identity, VS Code sign-in, etc.
var projectClient = new AIProjectClient(new Uri(projectEndpoint), new DefaultAzureCredential());

// ─── 5. Build the orchestrator agent ─────────────────────────────────────────
var repairAgent = new RepairPlannerAgent(
    projectClient,
    cosmosDb,
    faultMapping,
    modelDeploymentName,
    loggerFactory.CreateLogger<RepairPlannerAgent>());

// Register / update the PromptAgent definition in Azure AI Foundry (idempotent)
await repairAgent.EnsureAgentVersionAsync();

// ─── 6. Create a sample fault (mirrors FaultDiagnosisAgent output from Challenge 1) ──
var sampleFault = new DiagnosedFault
{
    MachineId  = "machine-001",
    FaultType  = "curing_temperature_excessive",
    RootCause  = "Thermostat calibration drift causing elevated curing temperatures",
    Severity   = "High",
    DetectedAt = DateTime.UtcNow.ToString("o"),   // ISO 8601
    Metadata   = new Dictionary<string, object?>
    {
        ["observedTemperature"]    = 179.2,
        ["threshold"]              = 178.0,
        ["MostLikelyRootCauses"]   = new[] { "Thermostat calibration drift", "Cooling system blockage" }
    }
};

// ─── 7. Run the full planning pipeline ───────────────────────────────────────
var workOrder = await repairAgent.PlanAndCreateWorkOrderAsync(sampleFault);

// ─── 8. Print results ────────────────────────────────────────────────────────
programLogger.LogInformation(
    "Saved work order {Number} (id={Id}, status={Status}, assignedTo={AssignedTo})",
    workOrder.WorkOrderNumber,
    workOrder.Id,
    workOrder.Status,
    workOrder.AssignedTo ?? "unassigned");

var json = JsonSerializer.Serialize(workOrder, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine(json);
