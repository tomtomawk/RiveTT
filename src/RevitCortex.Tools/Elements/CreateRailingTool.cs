using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Elements;

/// <summary>Creates an independent guardrail from a horizontal, connected path.</summary>
[ToolSafety(false, false)]
public sealed class CreateRailingTool : ICortexTool
{
    private const double MmPerFoot = 304.8;

    public string Name => "create_railing";
    public string Category => "Architecture";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Creates a native Revit guardrail from a connected horizontal path. A railing type and base level are required.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var document = ToolHelpers.GetDocument(session);
        if (document == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var path = input["path"] as JArray;
        var railingTypeId = input["railingTypeId"]?.Value<long>() ?? -1;
        var baseLevelId = input["baseLevelId"]?.Value<long>() ?? -1;
        if (path == null || path.Count < 2)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "path must contain at least two points");
        if (railingTypeId <= 0 || baseLevelId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "railingTypeId and baseLevelId are required");

        var railingType = document.GetElement(ToolHelpers.ToElementId(railingTypeId)) as RailingType;
        var level = document.GetElement(ToolHelpers.ToElementId(baseLevelId)) as Level;
        if (railingType == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"Railing type {railingTypeId} was not found");
        if (level == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"Level {baseLevelId} was not found");

        try
        {
            var points = path.Select(ToXyz).ToList();
            if (points.Zip(points.Skip(1), (a, b) => Math.Abs(a.Z - b.Z) > 1e-8).Any(x => x))
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "A railing path must be horizontal");

            var curveLoop = new CurveLoop();
            for (var index = 0; index < points.Count - 1; index++)
                curveLoop.Append(Line.CreateBound(points[index], points[index + 1]));

            using var transaction = new Transaction(document, "MCPRVTT27: Create Railing");
            transaction.Start();
            var railing = Railing.Create(document, curveLoop, railingType.Id, level.Id);
            if (transaction.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, "Revit rolled back railing creation");

            return CortexResult<object>.Ok(new
            {
                railingId = ToolHelpers.GetElementIdValue(railing.Id),
                railingType = railingType.Name,
                level = level.Name
            });
        }
        catch (Exception exception)
        {
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Could not create railing: {exception.Message}");
        }
    }

    private static XYZ ToXyz(JToken point) => new(
        (point["x"]?.Value<double>() ?? 0) / MmPerFoot,
        (point["y"]?.Value<double>() ?? 0) / MmPerFoot,
        (point["z"]?.Value<double>() ?? 0) / MmPerFoot);
}
