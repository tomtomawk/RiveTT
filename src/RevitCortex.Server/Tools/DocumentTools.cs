using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Connection;

namespace RevitCortex.Server.Tools;

[McpServerToolType]
public static class DocumentTools
{
    [McpServerTool(Name = "save_document"), Description("Save the active Revit project at its current path. dryRun reports the path, the unsaved-changes state and any predictable blocker without writing.")]
    public static async Task<string> SaveDocument(
        RevitConnectionManager revit,
        [Description("Preview without saving. Default: false")] bool? dryRun = null,
        CancellationToken ct = default)
    {
        var request = new JObject();
        if (dryRun != null) request["dryRun"] = dryRun;
        return (await revit.ExecuteAsync("save_document", request, ct)).ToString();
    }

    [McpServerTool(Name = "save_as_document"), Description("Save the active Revit project to an absolute .rvt path (parameter name: targetPath). This DUPLICATES the open document - it does not create a blank project from a template. dryRun reports source path, target path, overwrite policy and blockers without writing. Every cached read is flushed on success, so a follow-up read returns the new path.")]
    public static async Task<string> SaveAsDocument(
        RevitConnectionManager revit,
        [Description("Absolute output .rvt path")] string? targetPath = null,
        [Description("Replace an existing file. Default false")] bool? overwrite = null,
        [Description("Preview without saving. Default: false")] bool? dryRun = null,
        CancellationToken ct = default)
    {
        // targetPath is optional at the schema level on purpose: a missing or
        // misnamed path must come back as a structured InvalidInput naming the
        // expected parameter, not as an opaque "error invoking save_as_document".
        var request = new JObject();
        if (targetPath != null) request["targetPath"] = targetPath;
        if (overwrite != null) request["overwrite"] = overwrite;
        if (dryRun != null) request["dryRun"] = dryRun;
        return (await revit.ExecuteAsync("save_as_document", request, ct)).ToString();
    }
}
