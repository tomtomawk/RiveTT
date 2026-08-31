using System;
using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.IFC;

/// <summary>
/// Reloads an existing IFC link, optionally from a new IFC file path.
/// </summary>
[ToolSafety(false, true, supportsDryRun: true)]
public class IfcReloadLinkTool : IRiveTTTool
{
    public string Name => "ifc_reload_link";
    public string Category => "IFC";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Reload an existing IFC link, optionally from a new IFC file path. Previews by default: reloading pulls "
        + "a file that may have changed under the model, and reloading FROM a new path rewrites the derived .RVT "
        + "cache next to it. Set dryRun=false to apply.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var linkTypeId = input["linkTypeId"]?.Value<long>() ?? 0;
        if (linkTypeId <= 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "linkTypeId is required",
                suggestion: "Provide the RevitLinkType element ID of the IFC link");

        var elementId = ToolHelpers.ToElementId(linkTypeId);
        var linkType = doc!.GetElement(elementId) as RevitLinkType;
        if (linkType == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                $"RevitLinkType {linkTypeId} not found");

        var currentRvtPath = "";
        try
        {
            var extRef = linkType.GetExternalFileReference();
            if (extRef != null)
                currentRvtPath = ModelPathUtils.ConvertModelPathToUserVisiblePath(extRef.GetAbsolutePath());
        }
        catch { /* path unavailable */ }

        var newIfcFilePath = input["newIfcFilePath"]?.Value<string>();
        var recreateLink = input["recreateLink"]?.Value<bool>() ?? true;

        if (!string.IsNullOrWhiteSpace(newIfcFilePath))
        {
            // H25-wave: gate caller paths; UNC allowed because linking from network shares
            // is a standard BIM workflow and the confirmation dialog shows the path.
            // The derived .RVT cache is written next to this path, so it is covered too.
            if (!PathSafety.TryResolveSafe(newIfcFilePath, out var safeIfcPath, out var pathError, allowUnc: true))
                return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    pathError,
                    suggestion: "Provide a path under Documents, Desktop, Downloads, the user profile, temp, or a network share");
            newIfcFilePath = safeIfcPath;

            if (!File.Exists(newIfcFilePath))
                return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    $"New IFC file not found: {newIfcFilePath}");
        }

        var description = string.IsNullOrWhiteSpace(newIfcFilePath)
            ? $"Reload IFC link '{linkType.Name}'"
            : $"Reload IFC link '{linkType.Name}' from '{Path.GetFileName(newIfcFilePath)}'";

        // The reload reads a file from disk and rewrites a .RVT cache: not something a
        // transaction rollback would undo, so the preview is declared, not probed.
        if (ToolHelpers.GetDryRun(input))
        {
            var fromNewPath = !string.IsNullOrWhiteSpace(newIfcFilePath);
            var cachePath = fromNewPath ? newIfcFilePath + ".RVT" : null;
            return ChangePreview.Declared(
                $"DryRun: would {description.Substring(0, 1).ToLowerInvariant()}{description.Substring(1)}.",
                new
                {
                    linkTypeId,
                    name = linkType.Name,
                    action = fromNewPath ? "reload_from_new_path" : "reload",
                    newIfcFilePath,
                    derivedRvtCache = cachePath,
                    derivedRvtCacheWouldBeOverwritten = cachePath != null && File.Exists(cachePath),
                    recreateLink
                },
                blockers: fromNewPath && !File.Exists(newIfcFilePath!)
                    ? new[] { $"No IFC file at the new path: {newIfcFilePath}" }
                    : null);
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(newIfcFilePath))
            {
                var revitFilePath = newIfcFilePath + ".RVT";
                var options = new RevitLinkOptions(false);
                // CreateFromIFC modifies the document and must run inside a transaction
                // (mirrors IfcLinkTool). Without it Revit throws "Cannot modify the document
                // outside of a transaction" (ultrareview C6).
                using (var tx = new Transaction(doc!, "RiveTT: Reload IFC Link"))
                {
                    var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
                    tx.Start();
                    RevitLinkType.CreateFromIFC(doc!, newIfcFilePath, revitFilePath, recreateLink, options);
                    if (tx.Commit() != TransactionStatus.Committed)
                        return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                            $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                            suggestion: "Fix the reported model errors and retry.");
                }
            }
            else
            {
                var result = linkType.Reload();
                if (result.LoadResult != LinkLoadResultType.LinkLoaded)
                    return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                        $"Reload failed with status: {result.LoadResult}");
            }

            return RiveTTResult<object>.Ok(new
            {
                linkTypeId,
                name = linkType.Name,
                action = string.IsNullOrWhiteSpace(newIfcFilePath) ? "reloaded" : "reloaded_from_new_path",
                newIfcFilePath,
            });
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"Failed to reload IFC link: {ex.Message}");
        }
    }
}
