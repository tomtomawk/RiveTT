using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RiveTT.Server.Connection;

namespace RiveTT.Server.Tools;

[McpServerToolType]
public static class NavigationTools
{
    [McpServerTool(Name = "open_file"), Description("Open and activate a file entirely in Revit, available while the RiveTT write lock is closed. "
        + "RVT/RFA/RTE open directly. RFT creates a new family; IFC converts to a project, each in a temporary working folder without overwriting the source. "
        + "Every later call targets the activated document. DWG/PDF/images need dedicated import or link tools; they are not standalone Revit documents.")]
    public static async Task<string> OpenFile(RevitConnectionManager revit,
        [Description("Absolute local, mapped-drive or UNC path: .rvt, .rfa, .rte, .rft or .ifc")] string filePath,
        [Description("RVT only: detach from central, preserving worksets. Default false")] bool detachFromCentral = false,
        [Description("Preview without opening or creating working files. Default false")] bool dryRun = false,
        CancellationToken ct = default)
    {
        var request = new JObject { ["filePath"] = filePath, ["detachFromCentral"] = detachFromCentral, ["dryRun"] = dryRun };
        return (await revit.ExecuteAsync("open_file", request, ct)).ToString();
    }

    [McpServerTool(Name = "activate_view"), Description("Make a view or sheet the active view in the current Revit document. "
        + "Available while RiveTT is locked; no transaction or model edit. Returns the actual active view after switching. Templates and internal views are refused.")]
    public static async Task<string> ActivateView(RevitConnectionManager revit,
        [Description("Revit element ID of the view or sheet in the currently active document")] long viewId,
        [Description("Preview without changing the active view. Default false")] bool dryRun = false,
        CancellationToken ct = default)
    {
        var request = new JObject { ["viewId"] = viewId, ["dryRun"] = dryRun };
        return (await revit.ExecuteAsync("activate_view", request, ct)).ToString();
    }
}
