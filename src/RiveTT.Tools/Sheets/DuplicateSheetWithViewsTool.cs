using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Sheets;

/// <summary>
/// Duplicates a sheet and its placed views with configurable duplication options. Shares its
/// implementation with duplicate_sheet_with_content (see <see cref="SheetDuplicator"/>); it
/// differs only by NOT copying the loose sheet annotations unless copyAnnotations=true.
/// </summary>
[ToolSafety(false, false, supportsDryRun: true)]
public class DuplicateSheetWithViewsTool : IRiveTTTool
{
    public string Name => "duplicate_sheet_with_views";
    public string Category => "Sheets";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Duplicates a sheet N times with configurable view duplication (viewDuplicateOption: Duplicate | "
        + "DuplicateWithDetailing | DuplicateAsDependent). Copies ALL title blocks, the sheet's own parameters "
        + "(Dossier, Indice_manuel...) and its sheet collection. newNumber/newName set the copy's identity "
        + "(without newNumber or a prefix/suffix the number is the source's and Revit refuses it). Loose sheet "
        + "annotations are copied only with copyAnnotations=true. Same parameters as duplicate_sheet_with_content.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");
        // dryRun is read here, where the tool declares supportsDryRun; SheetDuplicator runs the probe.
        var dryRun = ToolHelpers.GetDryRun(input);
        return SheetDuplicator.Run(input, doc, Name, copyAnnotationsDefault: false, dryRun: dryRun);
    }
}
