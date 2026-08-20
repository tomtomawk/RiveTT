using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Connection;

namespace RevitCortex.Server.Tools;

[McpServerToolType]
public static class DocumentTools
{
    [McpServerTool(Name = "save_document"), Description("Save the active Revit project at its current path.")]
    public static async Task<string> SaveDocument(RevitConnectionManager revit, CancellationToken ct = default)
        => (await revit.ExecuteAsync("save_document", new JObject(), ct)).ToString();

    [McpServerTool(Name = "save_as_document"), Description("Save the active Revit project to an absolute .rvt path.")]
    public static async Task<string> SaveAsDocument(
        RevitConnectionManager revit,
        [Description("Absolute output .rvt path")] string targetPath,
        [Description("Replace an existing file. Default false")] bool? overwrite = null,
        CancellationToken ct = default)
    {
        var request = new JObject { ["targetPath"] = targetPath };
        if (overwrite != null) request["overwrite"] = overwrite;
        return (await revit.ExecuteAsync("save_as_document", request, ct)).ToString();
    }
}
