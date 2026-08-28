using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Elements;

/// <summary>
/// Groups elements into an AssemblyInstance (prefabrication/shop drawings), or splits
/// elements into Parts (demolition/phasing sequencing) — neither had an entry point.
/// AssemblyInstance.Create and PartUtils.CreateParts are both verified, real Revit
/// API calls; low interest for design work but real for prefabrication workflows.
/// </summary>
[ToolSafety(false, false)]
public class CreateAssemblyTool : IRiveTTTool
{
    public string Name => "create_assembly";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Groups elements into an AssemblyInstance (prefabrication), or splits them into Parts. " +
        "action=create_assembly|create_parts. create_assembly needs elementIds and categoryId (the assembly's " +
        "own category, e.g. OST_Assemblies or a discipline-specific assembly category). create_parts needs " +
        "elementIds; Revit builds the resulting parts during the next regeneration, not immediately.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var action = (input["action"]?.Value<string>() ?? "").ToLowerInvariant();
        var elementIds = input["elementIds"]?.ToObject<List<long>>() ?? new List<long>();
        if (elementIds.Count == 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "elementIds (non-empty array) is required");

        var ids = elementIds.Select(ToolHelpers.ToElementId).ToList();

        try
        {
            return action switch
            {
                "create_assembly" => CreateAssembly(doc, input, ids),
                "create_parts" => CreateParts(doc, ids),
                _ => RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    $"Unsupported action: {action}",
                    suggestion: "Use: create_assembly | create_parts")
            };
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static RiveTTResult<object> CreateAssembly(Document doc, JObject input, List<ElementId> ids)
    {
        var categoryName = input["categoryName"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(categoryName))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "categoryName is required");

        var categoryId = CategoryResolver.ResolveToId(doc, categoryName!);
        if (categoryId == null || categoryId == ElementId.InvalidElementId)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"Category '{categoryName}' could not be resolved in this document");

        using var tx = new Transaction(doc, "RiveTT: Create Assembly");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        AssemblyInstance assembly;
        try
        {
            assembly = AssemblyInstance.Create(doc, ids, categoryId);
        }
        catch (Exception ex)
        {
            tx.RollBack();
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"AssemblyInstance.Create failed: {ex.Message}",
                suggestion: "All elements must be assembly-eligible and not already part of another assembly.");
        }

        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}");

        return RiveTTResult<object>.Ok(new
        {
            assemblyId = ToolHelpers.GetElementIdValue(assembly.Id),
            memberCount = ids.Count
        });
    }

    private static RiveTTResult<object> CreateParts(Document doc, List<ElementId> ids)
    {
        if (!PartUtils.AreElementsValidForCreateParts(doc, ids))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "One or more elements are not valid for Parts creation",
                suggestion: "Only physical, part-eligible model elements (walls, floors, roofs...) qualify.");

        using var tx = new Transaction(doc, "RiveTT: Create Parts");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        try
        {
            PartUtils.CreateParts(doc, ids);
        }
        catch (Exception ex)
        {
            tx.RollBack();
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown, $"PartUtils.CreateParts failed: {ex.Message}");
        }

        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}");

        return RiveTTResult<object>.Ok(new
        {
            message = "Parts will be built at the next regeneration; PartUtils.CreateParts only schedules them.",
            sourceElementCount = ids.Count
        });
    }
}
