using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Elements;

[ToolSafety(false, false, supportsDryRun: true)]
public class ModifyElementTool : IRiveTTTool
{
    public string Name => "modify_element";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Move, rotate, mirror, or copy elements. Rotate is about the Z axis by default, or any axis via rotationAxis {x,y,z}. set_endpoints shortens or extends a straight wall or beam from a new start and/or end {x,y[,z]} in mm, keeping its hosted doors, windows and attached dimensions (delete + create destroys them).";
    // 1 foot = MmPerFoot mm

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var elementIds = input["elementIds"]?.ToObject<long[]>();
        if (elementIds == null || elementIds.Length == 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "elementIds is required");

        var action = input["action"]?.Value<string>()?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(action))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "action is required",
                suggestion: "Supported actions: move, rotate, mirror, copy");

        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "No active document in session");

        // Resolve valid element ids
        var revitIds = elementIds
            .Select(id =>
            {
                return new ElementId(id);
            })
            .Where(id => doc.GetElement(id) != null)
            .ToList();

        if (revitIds.Count == 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "No valid elements found for the provided elementIds");

        try
        {
            ICollection<ElementId>? newElementIds = null;

            var dryRun = ToolHelpers.GetDryRun(input);
            // A preview must COMMIT to see what a commit would raise: Revit checks "elements that
            // cannot stay attached to each other" and the like during commit failure processing, which
            // a plain RollBack never reaches. The probe therefore commits inside a group and rolls
            // the whole group back (field report 0.5.2: a green dryRun, then TransactionFailed).
            using var probeGroup = dryRun ? new TransactionGroup(doc, "RiveTT: Modify Elements (probe)") : null;
            probeGroup?.Start();
            using var tx = new Transaction(doc, $"RiveTT: Modify Elements - {action}");
            tx.Start();
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            try
            {
                switch (action)
                {
                    case "move":
                        ExecuteMove(doc, revitIds, input);
                        break;
                    case "rotate":
                        ExecuteRotate(doc, revitIds, input);
                        break;
                    case "mirror":
                        ExecuteMirror(doc, revitIds, input);
                        break;
                    case "copy":
                        newElementIds = ExecuteCopy(doc, revitIds, input);
                        break;
                    case "set_endpoints":
                        ExecuteSetEndpoints(doc, revitIds, input);
                        break;
                    default:
                        tx.RollBack();
                        return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                            $"Unknown action '{action}'",
                            suggestion: "Supported actions: move, rotate, mirror, copy, set_endpoints");
                }

                // Committed in BOTH modes: under dryRun the enclosing group is what undoes it, after
                // the payload below has read the elements it describes.
                if (tx.Commit() != TransactionStatus.Committed)
                {
                    if (probeGroup?.GetStatus() == TransactionStatus.Started)
                        probeGroup.RollBack();
                    return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                        (dryRun ? "DryRun: this call would fail. " : "") +
                        $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                        suggestion: "Fix the reported model errors and retry. " +
                                    (dryRun ? "Nothing was changed." : ""));
                }
            }
            catch
            {
                if (tx.GetStatus() == TransactionStatus.Started)
                    tx.RollBack();
                if (probeGroup?.GetStatus() == TransactionStatus.Started)
                    probeGroup.RollBack();
                throw;
            }

            var resultData = BuildResult(action, revitIds.Count, newElementIds);
            if (dryRun)
            {
                if (probeGroup?.GetStatus() == TransactionStatus.Started)
                    probeGroup.RollBack();
                return ChangePreview.Probed(
                    "DryRun: the operation ran inside a transaction and was rolled back. The "
                    + "model is untouched; what follows is what Revit produced.",
                    resultData);
            }

            return RiveTTResult<object>.Ok(resultData);
        }
        catch (ArgumentException ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, ex.Message);
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"Modify element failed: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the ids and the target with a read tool before retrying, and narrow the "
                    + "call if it covered many elements. The full call, its duration and this error are "
                    + "in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }

    // ── Action implementations ─────────────────────────────────────────────

    private static void ExecuteMove(Document doc, List<ElementId> elementIds, JObject input)
    {
        var translationToken = input["translation"];
        if (translationToken == null)
            throw new ArgumentException("translation is required for move action. Provide {x, y, z} in mm.");

        var translation = ParseXYZ(translationToken, "translation");
        ElementTransformUtils.MoveElements(doc, elementIds, translation);
    }

    private static void ExecuteRotate(Document doc, List<ElementId> elementIds, JObject input)
    {
        var centerToken = input["rotationCenter"];
        if (centerToken == null)
            throw new ArgumentException("rotationCenter is required for rotate action. Provide {x, y, z} in mm.");

        var angleToken = input["rotationAngle"];
        if (angleToken == null)
            throw new ArgumentException("rotationAngle is required for rotate action (degrees).");

        var center = ParseXYZ(centerToken, "rotationCenter");
        var angleDegrees = angleToken.Value<double>();
        var angleRadians = angleDegrees * Math.PI / 180.0;

        // Rotation axis: default Z at the center, or an arbitrary direction vector
        // via rotationAxis {x,y,z} (e.g. {x:1,y:0,z:0} to tilt about the X axis).
        var axisToken = input["rotationAxis"];
        XYZ axisDir = XYZ.BasisZ;
        if (axisToken != null && axisToken.Type != JTokenType.Null)
        {
            var ax = axisToken["x"]?.Value<double>() ?? 0;
            var ay = axisToken["y"]?.Value<double>() ?? 0;
            var az = axisToken["z"]?.Value<double>() ?? 1;
            var dir = new XYZ(ax, ay, az);
            if (dir.GetLength() > 1e-9) axisDir = dir.Normalize();
        }

        var axis = Line.CreateBound(center, center + axisDir);
        ElementTransformUtils.RotateElements(doc, elementIds, axis, angleRadians);
    }

    private static void ExecuteMirror(Document doc, List<ElementId> elementIds, JObject input)
    {
        var originToken = input["mirrorPlaneOrigin"];
        if (originToken == null)
            throw new ArgumentException("mirrorPlaneOrigin is required for mirror action. Provide {x, y, z} in mm.");

        var normalToken = input["mirrorPlaneNormal"];
        if (normalToken == null)
            throw new ArgumentException("mirrorPlaneNormal is required for mirror action. Provide {x, y, z} (unit vector, e.g. {x:1,y:0,z:0} for YZ plane).");

        var origin = ParseXYZ(originToken, "mirrorPlaneOrigin");

        // Normal is a direction vector — convert mm→feet only for position vectors;
        // for direction vectors, just read and normalize without unit conversion
        var nx = normalToken["x"]?.Value<double>() ?? 0;
        var ny = normalToken["y"]?.Value<double>() ?? 0;
        var nz = normalToken["z"]?.Value<double>() ?? 0;
        var normal = new XYZ(nx, ny, nz).Normalize();

        var mirrorPlane = Plane.CreateByNormalAndOrigin(normal, origin);
        ElementTransformUtils.MirrorElements(doc, elementIds, mirrorPlane, false);
    }

    private static ICollection<ElementId> ExecuteCopy(Document doc, List<ElementId> elementIds, JObject input)
    {
        var offsetToken = input["copyOffset"];
        if (offsetToken == null)
            throw new ArgumentException("copyOffset is required for copy action. Provide {x, y, z} in mm.");

        var offset = ParseXYZ(offsetToken, "copyOffset");
        return ElementTransformUtils.CopyElements(doc, elementIds, offset);
    }

    /// <summary>
    /// Moves the end points of a straight element (a wall, a beam) by rewriting its location line:
    /// the equivalent of dragging a wall's end grip. The element is kept, so the doors, windows and
    /// openings it hosts and the dimensions attached to it stay, where delete + create destroyed
    /// 5 dimensions, 2 doors and 1 opening in one session (field report 0.5.2).
    /// start / end are {x, y[, z]} in mm; z defaults to the current end point's own, and an omitted
    /// end stays where it is.
    /// </summary>
    private static void ExecuteSetEndpoints(Document doc, List<ElementId> elementIds, JObject input)
    {
        var startToken = input["start"];
        var endToken = input["end"];
        if ((startToken == null || startToken.Type == JTokenType.Null)
            && (endToken == null || endToken.Type == JTokenType.Null))
            throw new ArgumentException("set_endpoints needs start and/or end: {x, y} in mm (z optional).");

        foreach (var id in elementIds)
        {
            var element = doc.GetElement(id);
            if (element?.Location is not LocationCurve location || location.Curve is not Line line)
                throw new ArgumentException(
                    $"Element {id.Value} has no straight location line: set_endpoints applies to straight walls, beams and similar elements.");

            var start = line.GetEndPoint(0);
            var end = line.GetEndPoint(1);
            var newStart = startToken != null && startToken.Type != JTokenType.Null ? ParsePlanPoint(startToken, "start", start.Z) : start;
            var newEnd = endToken != null && endToken.Type != JTokenType.Null ? ParsePlanPoint(endToken, "end", end.Z) : end;
            if (newStart.DistanceTo(newEnd) < 1e-6)
                throw new ArgumentException($"Element {id.Value}: start and end would coincide.");

            location.Curve = Line.CreateBound(newStart, newEnd);
        }
    }

    private static XYZ ParsePlanPoint(JToken token, string paramName, double currentZFeet)
    {
        if (token["x"] == null || token["y"] == null)
            throw new ArgumentException($"'{paramName}' must be a {{x, y}} object in mm (z optional).");
        var x = token["x"]!.Value<double>() / MmPerFoot;
        var y = token["y"]!.Value<double>() / MmPerFoot;
        var z = token["z"] != null ? token["z"]!.Value<double>() / MmPerFoot : currentZFeet;
        return new XYZ(x, y, z);
    }

    // ── Result builder ─────────────────────────────────────────────────────

    private static object BuildResult(string action, int elementCount, ICollection<ElementId>? newElementIds)
    {
        if (action == "copy" && newElementIds != null)
        {
            var newIds = newElementIds.Select(id =>
            {
                return id.Value;
            }).ToArray();

            return new
            {
                message = $"Successfully copied {elementCount} element(s), created {newIds.Length} new element(s)",
                action,
                elementCount,
                newElementIds = newIds
            };
        }

        return new
        {
            message = $"Successfully executed '{action}' on {elementCount} element(s)",
            action,
            elementCount
        };
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a JSON {x,y,z} token (coordinates in mm) into Revit's internal foot-based XYZ.
    /// </summary>
    private static XYZ ParseXYZ(JToken token, string paramName)
    {
        if (token == null || token.Type == JTokenType.Null)
            throw new ArgumentException($"'{paramName}' must be a {{x, y, z}} object in mm.");

        var x = token["x"]?.Value<double>() ?? 0;
        var y = token["y"]?.Value<double>() ?? 0;
        var z = token["z"]?.Value<double>() ?? 0;
        return new XYZ(x / MmPerFoot, y / MmPerFoot, z / MmPerFoot);
    }
}
