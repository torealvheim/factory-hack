using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using RepairPlanner.Models;
using RepairPlanner.Services;

namespace RepairPlanner;

/// <summary>
/// Orchestrates the repair planning workflow:
///   1. Look up required skills + parts from FaultMappingService
///   2. Query available technicians and stocked parts from Cosmos DB
///   3. Register (or update) the Foundry PromptAgent
///   4. Invoke the agent with a rich context prompt
///   5. Parse the JSON response into a WorkOrder
///   6. Save the WorkOrder to Cosmos DB
/// </summary>
public sealed class RepairPlannerAgent(
    // Primary constructor — parameters become implicit fields accessible throughout
    // the class body. (Like Python's __init__ that assigns self.x = x.)
    AIProjectClient projectClient,
    CosmosDbService cosmosDb,
    IFaultMappingService faultMapping,
    string modelDeploymentName,
    ILogger<RepairPlannerAgent> logger)
{
    private const string AgentName = "RepairPlannerAgent";

    private const string AgentInstructions = """
        You are a Repair Planner Agent for tire manufacturing equipment.
        Given a diagnosed fault, available technicians, and available parts,
        generate a complete repair plan as a single valid JSON object.

        Output JSON with these exact fields (no Markdown, no prose — JSON only):
        {
          "workOrderNumber": string,
          "machineId": string,
          "title": string,
          "description": string,
          "type": "corrective" | "preventive" | "emergency",
          "priority": "critical" | "high" | "medium" | "low",
          "status": "open",
          "assignedTo": string | null,
          "notes": string,
          "estimatedDuration": integer,
          "partsUsed": [{ "partId": string, "partNumber": string, "quantity": integer }],
          "tasks": [{
            "sequence": integer,
            "title": string,
            "description": string,
            "estimatedDurationMinutes": integer,
            "requiredSkills": [string],
            "safetyNotes": string
          }]
        }

        Rules:
        - assignedTo: set to the id of the most qualified available technician, or null if none.
        - partsUsed: include only parts from the provided parts list; empty array if none.
        - estimatedDuration and estimatedDurationMinutes MUST be integers (minutes), never strings.
        - tasks must be ordered and actionable.
        - priority must reflect the fault severity: Critical→"critical", High→"high", Medium→"medium", Low→"low".
        """;

    // JsonSerializerOptions used when parsing the agent's JSON response.
    // AllowReadingFromString handles cases where the LLM returns numbers as
    // strings (e.g. "60" instead of 60).
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    // -------------------------------------------------------------------------
    // Agent registration
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registers (or updates) the PromptAgent definition in Azure AI Foundry.
    /// Safe to call on every startup — CreateAgentVersionAsync is idempotent.
    /// </summary>
    public async Task EnsureAgentVersionAsync(CancellationToken ct = default)
    {
        logger.LogInformation("Registering agent '{Name}' with model '{Model}'...", AgentName, modelDeploymentName);

        var definition = new PromptAgentDefinition(model: modelDeploymentName)
        {
            Instructions = AgentInstructions
        };

        await projectClient.Agents.CreateAgentVersionAsync(
            AgentName,
            new AgentVersionCreationOptions(definition),
            ct);

        logger.LogInformation("Agent '{Name}' registered successfully.", AgentName);
    }

    // -------------------------------------------------------------------------
    // Main orchestration
    // -------------------------------------------------------------------------

    /// <summary>
    /// Full pipeline: look up data → call agent → parse response → save work order.
    /// </summary>
    public async Task<WorkOrder> PlanAndCreateWorkOrderAsync(
        DiagnosedFault fault,
        CancellationToken ct = default)
    {
        logger.LogInformation(
            "Planning repair for machine '{MachineId}', fault '{FaultType}', severity '{Severity}'.",
            fault.MachineId, fault.FaultType, fault.Severity);

        // -- Step 1: Resolve required skills and part numbers ------------------
        var requiredSkills = faultMapping.GetRequiredSkills(fault.FaultType);
        var requiredPartNumbers = faultMapping.GetRequiredParts(fault.FaultType);

        logger.LogInformation("Required skills: {Skills}", string.Join(", ", requiredSkills));
        logger.LogInformation("Required parts:  {Parts}", string.Join(", ", requiredPartNumbers));

        // -- Step 2: Fetch matching data from Cosmos DB ------------------------
        // Run both queries concurrently (like Python's asyncio.gather).
        var technicianTask = cosmosDb.GetAvailableTechniciansBySkillsAsync(requiredSkills, ct);
        var partsTask = requiredPartNumbers.Count > 0
            ? cosmosDb.GetPartsByNumbersAsync(requiredPartNumbers, ct)
            : Task.FromResult(new List<Part>());

        await Task.WhenAll(technicianTask, partsTask);

        var technicians = technicianTask.Result;
        var parts = partsTask.Result;

        logger.LogInformation("Found {Tech} technician(s) and {Parts} part(s) in stock.",
            technicians.Count, parts.Count);

        // -- Step 3: Build the context prompt ----------------------------------
        var prompt = BuildPrompt(fault, technicians, parts, requiredSkills, requiredPartNumbers);

        // -- Step 4: Invoke the Foundry agent ----------------------------------
        logger.LogInformation("Invoking Foundry agent '{Name}'...", AgentName);

        var agent = projectClient.GetAIAgent(name: AgentName);
        var agentResponse = await agent.RunAsync(prompt, thread: null, options: null);

        // ?? means "if null, use empty string" (like Python: text or "")
        var rawJson = agentResponse.Text ?? string.Empty;
        logger.LogDebug("Agent raw response:\n{Json}", rawJson);

        // -- Step 5: Parse JSON → WorkOrder ------------------------------------
        var workOrder = ParseWorkOrder(rawJson, fault);

        // Apply sensible defaults for any fields the LLM may have omitted.
        // ??= means "assign only if the current value is null"
        workOrder.Priority ??= MapSeverityToPriority(fault.Severity);
        workOrder.WorkOrderNumber = workOrder.WorkOrderNumber is { Length: > 0 }
            ? workOrder.WorkOrderNumber
            : $"WO-{fault.MachineId}-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        workOrder.Id = workOrder.WorkOrderNumber;
        workOrder.MachineId = fault.MachineId;
        workOrder.DiagnosedFault = fault;
        workOrder.CreatedAt = DateTime.UtcNow.ToString("o");

        // -- Step 6: Persist to Cosmos DB -------------------------------------
        var saved = await cosmosDb.SaveWorkOrderAsync(workOrder, ct);

        logger.LogInformation("Work order '{Id}' saved with priority '{Priority}'.",
            saved.Id, saved.Priority);

        return saved;
    }

    // -------------------------------------------------------------------------
    // Prompt construction
    // -------------------------------------------------------------------------

    private static string BuildPrompt(
        DiagnosedFault fault,
        List<Technician> technicians,
        List<Part> parts,
        IReadOnlyList<string> requiredSkills,
        IReadOnlyList<string> requiredPartNumbers)
    {
        var sb = new StringBuilder();

        sb.AppendLine("## Diagnosed Fault");
        sb.AppendLine($"- MachineId: {fault.MachineId}");
        sb.AppendLine($"- FaultType: {fault.FaultType}");
        sb.AppendLine($"- RootCause: {fault.RootCause}");
        sb.AppendLine($"- Severity: {fault.Severity}");
        sb.AppendLine($"- DetectedAt: {fault.DetectedAt}");

        sb.AppendLine();
        sb.AppendLine("## Required Skills");
        foreach (var skill in requiredSkills)
            sb.AppendLine($"- {skill}");

        sb.AppendLine();
        sb.AppendLine("## Required Part Numbers");
        if (requiredPartNumbers.Count == 0)
        {
            sb.AppendLine("- (none)");
        }
        else
        {
            foreach (var pn in requiredPartNumbers)
                sb.AppendLine($"- {pn}");
        }

        sb.AppendLine();
        sb.AppendLine("## Available Technicians");
        if (technicians.Count == 0)
        {
            sb.AppendLine("- (none available)");
        }
        else
        {
            foreach (var t in technicians)
            {
                sb.AppendLine($"- id={t.Id} name={t.Name} department={t.Department}");
                sb.AppendLine($"  skills: {string.Join(", ", t.Skills)}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Available Parts In Stock");
        if (parts.Count == 0)
        {
            sb.AppendLine("- (none in stock)");
        }
        else
        {
            foreach (var p in parts)
                sb.AppendLine($"- id={p.Id} partNumber={p.PartNumber} name={p.Name} qty={p.QuantityInStock}");
        }

        sb.AppendLine();
        sb.AppendLine("Generate the repair plan JSON now.");

        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // Response parsing
    // -------------------------------------------------------------------------

    private WorkOrder ParseWorkOrder(string rawJson, DiagnosedFault fault)
    {
        // Strip Markdown code fences (```json ... ```) that some models add.
        var json = StripMarkdownCodeFences(rawJson).Trim();

        if (string.IsNullOrWhiteSpace(json))
        {
            logger.LogWarning("Agent returned empty response; using fallback work order.");
            return CreateFallbackWorkOrder(fault);
        }

        try
        {
            var workOrder = JsonSerializer.Deserialize<WorkOrder>(json, JsonOptions);
            if (workOrder is null)
            {
                logger.LogWarning("Deserialization returned null; using fallback work order.");
                return CreateFallbackWorkOrder(fault);
            }

            return workOrder;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse agent JSON response. Raw:\n{Json}", rawJson);
            return CreateFallbackWorkOrder(fault);
        }
    }

    private static string StripMarkdownCodeFences(string text)
    {
        // Remove ```json ... ``` or ``` ... ``` wrappers.
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0)
                trimmed = trimmed[(firstNewline + 1)..];

            if (trimmed.EndsWith("```"))
                trimmed = trimmed[..^3];
        }
        return trimmed;
    }

    private static WorkOrder CreateFallbackWorkOrder(DiagnosedFault fault) => new()
    {
        WorkOrderNumber = $"WO-{fault.MachineId}-{DateTime.UtcNow:yyyyMMdd-HHmmss}",
        MachineId = fault.MachineId,
        Title = $"Repair: {fault.FaultType}",
        Description = fault.RootCause,
        Type = "corrective",
        Priority = MapSeverityToPriority(fault.Severity),
        Status = "open",
        Notes = "Auto-generated fallback — agent response could not be parsed.",
        EstimatedDuration = 60,
    };

    private static string MapSeverityToPriority(string severity) => severity.ToLowerInvariant() switch
    {
        "critical" => "critical",
        "high"     => "high",
        "medium"   => "medium",
        _          => "low",
    };
}
