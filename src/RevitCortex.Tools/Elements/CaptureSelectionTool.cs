using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Elements;

[ToolSafety(true, false)]
public sealed class CaptureSelectionTool : ICortexTool
{
    public string Name => "capture_selection";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Capture explicit IDs or the current Revit selection as a temporary, reusable selection token.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = ToolHelpers.GetDocument(session);
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var ids = input["elementIds"]?.ToObject<long[]>()
            ?? new UIDocument(doc).Selection.GetElementIds()
                .Select(ToolHelpers.GetElementIdValue).ToArray();
        ids = ids.Where(id => id > 0 && doc.GetElement(ToolHelpers.ToElementId(id)) != null)
            .Distinct().ToArray();
        if (ids.Length == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No valid elements were provided or selected");

        var ttlMinutes = Math.Clamp(input["ttlMinutes"]?.Value<int>() ?? 15, 1, 120);
        var token = ElementScopeResolver.Capture(session, ids, TimeSpan.FromMinutes(ttlMinutes),
            out var expiresAtUtc);
        return CortexResult<object>.Ok(new
        {
            selectionToken = token,
            elementCount = ids.Length,
            expiresAtUtc = expiresAtUtc.ToString("o")
        });
    }
}
