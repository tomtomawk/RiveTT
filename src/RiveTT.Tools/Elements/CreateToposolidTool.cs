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
/// Creates a Toposolid (site/ground surface) from a boundary loop — Toposolid.Create
/// is Revit's current topography element (2024+; this connector's floor is 2026, so
/// no version gate is needed). Nothing built a site surface or a hard/soft landscaping
/// platform at all before this.
/// </summary>
[ToolSafety(false, false)]
public class CreateToposolidTool : IRiveTTTool
{
    public string Name => "create_toposolid";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Creates a Toposolid (site/ground surface) from a closed boundary loop (Toposolid.Create). " +
        "curves is [{type:line|arc, start{x,y,z}, end{x,y,z}, mid?{x,y,z}}] in mm, forming a closed loop. " +
        "toposolidTypeId and levelId are required — list types with list_system_types(category: \"OST_Toposolid\").";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var typeIdLong = input["toposolidTypeId"]?.Value<long?>() ?? 0;
        var levelIdLong = input["levelId"]?.Value<long?>() ?? 0;
        var curvesArray = input["curves"] as JArray;
        if (typeIdLong <= 0 || levelIdLong <= 0 || curvesArray == null || curvesArray.Count == 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "toposolidTypeId, levelId, and a non-empty curves array (closed loop, mm) are required");

        var toposolidType = doc.GetElement(ToolHelpers.ToElementId(typeIdLong)) as ElementType;
        if (toposolidType == null || toposolidType.Category?.Id != new ElementId(BuiltInCategory.OST_Toposolid))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"toposolidTypeId {typeIdLong} is not an OST_Toposolid type",
                suggestion: "List valid ids with list_system_types(category: \"OST_Toposolid\").");

        var level = doc.GetElement(ToolHelpers.ToElementId(levelIdLong)) as Level;
        if (level == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, $"levelId {levelIdLong} is not a Level");

        var curves = CurveSpecHelpers.ParseCurveSpecsMm(curvesArray, out var curveError);
        if (curveError != null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, curveError);

        CurveLoop loop;
        try
        {
            loop = CurveLoop.Create(curves);
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"curves do not form a valid closed loop: {ex.Message}");
        }

        using var tx = new Transaction(doc, "RiveTT: Create Toposolid");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        Toposolid toposolid;
        try
        {
            toposolid = Toposolid.Create(doc, new List<CurveLoop> { loop }, toposolidType.Id, level.Id);
        }
        catch (Exception ex)
        {
            tx.RollBack();
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"Toposolid.Create failed: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the ids and the target with a read tool before retrying, and narrow the "
                    + "call if it covered many elements. The full call, its duration and this error are "
                    + "in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }

        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}");

        return RiveTTResult<object>.Ok(new
        {
            toposolidId = ToolHelpers.GetElementIdValue(toposolid.Id),
            levelName = level.Name
        });
    }
}
