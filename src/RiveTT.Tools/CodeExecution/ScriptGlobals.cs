using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace RiveTT.Tools.CodeExecution;

/// <summary>
/// Variables injected into every script executed by send_code_to_revit.
/// Property names are lowercase to match CLAUDE.md conventions.
/// Used by the .NET 10 Roslyn execution path.
/// </summary>
public class ScriptGlobals
{
    public Document document { get; set; } = null!;
    public UIDocument uiDocument { get; set; } = null!;
    public Autodesk.Revit.ApplicationServices.Application app { get; set; } = null!;

    /// <summary>
    /// The caller's scriptArgs object, never null. Lets a saved script run again with other
    /// values instead of being re-sent with the numbers edited in.
    /// </summary>
    public JObject scriptArgs { get; set; } = new JObject();
}
