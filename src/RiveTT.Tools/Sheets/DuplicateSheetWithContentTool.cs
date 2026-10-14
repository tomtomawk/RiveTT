using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;

namespace RiveTT.Tools.Sheets;

/// <summary>
/// Duplicates a sheet including all annotations and detail items. Shares its implementation
/// with duplicate_sheet_with_views (see <see cref="SheetDuplicator"/>); it differs only by
/// copying the loose sheet annotations unless copyAnnotations=false.
/// </summary>
[ToolSafety(false, false, supportsDryRun: true)]
public class DuplicateSheetWithContentTool : IRiveTTTool
{
    public string Name => "duplicate_sheet_with_content";
    public string Category => "Sheets";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Duplicates a sheet with its annotations and detail items, ALL its title blocks (the small cartouche too), its "
        + "own parameters (Dossier, Indice_manuel...) and its sheet collection. newNumber/newName set the copy's identity; "
        + "viewDuplicateOption (Duplicate | DuplicateWithDetailing | DuplicateAsDependent) chooses how placed views are "
        + "copied; viewNameSuffix replaces the default \" - <sheet number>\" added to copied view names (\"\" keeps "
        + "Revit's own copy name). Same parameters as duplicate_sheet_with_views.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");
        return SheetDuplicator.Run(input, doc, Name, copyAnnotationsDefault: true);
    }
}
