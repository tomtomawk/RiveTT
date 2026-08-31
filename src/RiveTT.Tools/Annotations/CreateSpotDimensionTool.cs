using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Annotations;

/// <summary>
/// Creates a spot elevation annotation (a level/coordinate callout on a point of an
/// element's geometry) — the "cote de niveau" missing from create_dimensions, which
/// only builds linear dimensions.
/// </summary>
[ToolSafety(false, false, supportsDryRun: true)]
public class CreateSpotDimensionTool : IRiveTTTool
{
    public string Name => "create_spot_dimension";
    public string Category => "Annotations";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Creates a spot elevation annotation at a point on an element's geometry (Document.Create.NewSpotElevation). " +
        "Provide elementId, point (mm, must lie on or very near the element's geometry), and the owning viewId " +
        "(defaults to the active view). Optional bend/end (mm) place the elbow and leader end; " +
        "when omitted they are derived from the view's up/right directions. hasLeader defaults to true.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var elementIdLong = input["elementId"]?.Value<long?>() ?? 0;
        var pointToken = input["point"];
        if (elementIdLong <= 0 || pointToken == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "elementId and point ({x,y,z} in mm) are required",
                suggestion: "Provide {\"elementId\": 123456, \"point\": {\"x\":0,\"y\":0,\"z\":3000}}");

        var viewIdLong = input["viewId"]?.Value<long?>() ?? 0;
        View? view = viewIdLong > 0
            ? doc.GetElement(ToolHelpers.ToElementId(viewIdLong)) as View
            : doc.ActiveView;
        if (view == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "Could not resolve target view");

        var elem = doc.GetElement(ToolHelpers.ToElementId(elementIdLong));
        if (elem == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, $"Element {elementIdLong} not found");

        var origin = ParseXYZ(pointToken);
        var reference = GetBestReference(elem, view, origin);
        if (reference == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"No dimensionable face/edge reference found near the given point on element {elementIdLong}");

        var hasLeader = input["hasLeader"]?.Value<bool?>() ?? true;
        var bend = ParseOptionalXYZ(input["bend"]) ?? origin + view.UpDirection * (200.0 / MmPerFoot);
        var end = ParseOptionalXYZ(input["end"]) ?? bend + view.RightDirection * (300.0 / MmPerFoot);

        var dryRun = ToolHelpers.GetDryRun(input);
        using var tx = new Transaction(doc, "RiveTT: Create Spot Dimension");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        try
        {
            // refPt matches origin, per the documented usage pattern (both are the point
            // on the beam/face location curve the spot elevation actually measures).
            var spot = doc.Create.NewSpotElevation(view, reference, origin, bend, end, origin, hasLeader);
            if (spot == null)
            {
                tx.RollBack();
                return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                    "SpotDimension.Create returned null");
            }

            // Built BEFORE the rollback: afterwards the elements this describes no longer
            // exist and reading a name off one throws. Captured verbatim from the real
            // return, so the preview cannot drift from what applying actually reports.
            var previewPayload = new
            {
                spotDimensionId = ToolHelpers.GetElementIdValue(spot.Id),
                viewId = ToolHelpers.GetElementIdValue(view.Id)
            };

            if (dryRun)
            {
                ChangePreview.Rollback(tx);
                return ChangePreview.Probed(
                    "DryRun: the operation ran inside a transaction and was rolled back. The model is "
                    + "untouched; what follows is what Revit produced.",
                    previewPayload);
            }

            if (tx.Commit() != TransactionStatus.Committed)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");

return RiveTTResult<object>.Ok(previewPayload);
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"create_spot_dimension could not complete: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the ids and the target with a read tool before retrying, and narrow the "
                    + "call if it covered many elements. The full call, its duration and this error are "
                    + "in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }

    /// <summary>Finds a face/edge reference on elem, preferring the one closest to nearPoint.</summary>
    private static Reference? GetBestReference(Element elem, View view, XYZ nearPoint)
    {
        var options = new Options { View = view, ComputeReferences = true };
        var geom = elem.get_Geometry(options);
        if (geom == null) return null;

        Reference? best = null;
        double bestDist = double.MaxValue;

        void Consider(Face face)
        {
            if (face.Reference == null) return;
            var proj = face.Project(nearPoint);
            var dist = proj != null ? proj.Distance : double.MaxValue;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = face.Reference;
            }
        }

        foreach (var obj in geom)
        {
            if (obj is Solid solid)
            {
                foreach (Face face in solid.Faces) Consider(face);
            }
            else if (obj is GeometryInstance gi)
            {
                foreach (var innerObj in gi.GetInstanceGeometry())
                {
                    if (innerObj is Solid innerSolid)
                        foreach (Face face in innerSolid.Faces) Consider(face);
                }
            }
        }

        return best;
    }

    private static XYZ ParseXYZ(JToken token)
    {
        var x = token["x"]?.Value<double>() ?? 0;
        var y = token["y"]?.Value<double>() ?? 0;
        var z = token["z"]?.Value<double>() ?? 0;
        return new XYZ(x / MmPerFoot, y / MmPerFoot, z / MmPerFoot);
    }

    private static XYZ? ParseOptionalXYZ(JToken? token) => token == null ? null : ParseXYZ(token);
}
