using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Project;

/// <summary>
/// Lists, reloads, or unloads linked Revit/CAD/IFC files.
/// </summary>
[ToolSafety(false, true)]
public class ManageLinksTool : IRiveTTTool
{
    public string Name => "manage_links";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Lists, reloads, reloads-from-path, unloads, or removes linked Revit/CAD/IFC files. Actions: list, reload, reload_from, unload, remove.";
    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var action = input["action"]?.Value<string>() ?? "list";
        var linkId = input["linkId"]?.Value<long>() ?? 0;

        try
        {
            return action.ToLowerInvariant() switch
            {
                "list" => ListLinks(doc),
                "reload" => ReloadLink(doc, linkId, session),
                "reload_from" => ReloadLinkFrom(doc, linkId, input, session),
                "unload" => UnloadLink(doc, linkId, session),
                "remove" => RemoveLink(doc, linkId, session),
                _ => RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    $"Unknown action: {action}", suggestion: "Use: list, reload, reload_from, unload, remove")
            };
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static RiveTTResult<object> ListLinks(Document doc)
    {
        var links = new List<object>();

        // Revit links
        foreach (var rl in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
        {
            var linkType = doc.GetElement(rl.GetTypeId()) as RevitLinkType;
            links.Add(new
            {
                id = ToolHelpers.GetElementIdValue(rl.Id),
                typeId = linkType != null ? ToolHelpers.GetElementIdValue(linkType.Id) : 0,
                name = rl.Name,
                linkType = "Revit",
                isLoaded = RevitLinkType.IsLoaded(doc, rl.GetTypeId()),
                path = linkType?.GetExternalFileReference()?.GetAbsolutePath()?.ToString() ?? ""
            });
        }

        // CAD links
        foreach (var import in new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).Cast<ImportInstance>())
        {
            links.Add(new
            {
                id = ToolHelpers.GetElementIdValue(import.Id),
                typeId = (long)0,
                name = import.Name,
                linkType = import.IsLinked ? "CAD_Link" : "CAD_Import",
                isLoaded = true,
                path = ""
            });
        }

        return RiveTTResult<object>.Ok(new { linkCount = links.Count, links });
    }

    private static RiveTTResult<object> ReloadLink(Document doc, long linkId, RiveTTSession session)
    {
        if (linkId <= 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "linkId required for reload");

        var linkInstance = doc.GetElement(new ElementId(linkId)) as RevitLinkInstance;
        if (linkInstance == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound, "Link not found");

        var linkType = doc.GetElement(linkInstance.GetTypeId()) as RevitLinkType;
        if (linkType == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound, "Link type not found");

        if (!session.RequestConfirmation("reload link", 1, linkInstance.Name))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RiveTT: Reload Link");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        linkType.Reload();
        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");

        return RiveTTResult<object>.Ok(new { linkId, name = linkInstance.Name, action = "reloaded" });
    }

    private static RiveTTResult<object> UnloadLink(Document doc, long linkId, RiveTTSession session)
    {
        if (linkId <= 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "linkId required for unload");

        var linkInstance = doc.GetElement(new ElementId(linkId)) as RevitLinkInstance;
        if (linkInstance == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound, "Link not found");

        var linkType = doc.GetElement(linkInstance.GetTypeId()) as RevitLinkType;
        if (linkType == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound, "Link type not found");

        if (!session.RequestConfirmation("unload link", 1, linkInstance.Name))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RiveTT: Unload Link");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        linkType.Unload(null);
        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");

        return RiveTTResult<object>.Ok(new { linkId, name = linkInstance.Name, action = "unloaded" });
    }

    private static RiveTTResult<object> ReloadLinkFrom(Document doc, long linkId, JObject input, RiveTTSession session)
    {
        if (linkId <= 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "linkId required for reload_from");

        var newPath = input["newPath"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(newPath))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "newPath required for reload_from", suggestion: "Provide the absolute path to reload the link from");

        // Absorbed from the retired reload_linked_file_from, which had this guard and this
        // tool did not: UNC allowed because linking models from network shares is a standard
        // BIM workflow and the confirmation dialog shows the path.
        if (!PathSafety.TryResolveSafe(newPath, out var safePath, out var pathError, allowUnc: true))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                pathError,
                suggestion: "Provide a path under Documents, Desktop, Downloads, the user profile, temp, or a network share");
        newPath = safePath;

        var linkInstance = doc.GetElement(ToolHelpers.ToElementId(linkId)) as RevitLinkInstance;
        if (linkInstance == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound, "Link not found");

        var linkType = doc.GetElement(linkInstance.GetTypeId()) as RevitLinkType;
        if (linkType == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound, "Link type not found");

        if (!session.RequestConfirmation("reload link from new path", 1, linkInstance.Name))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Cancelled, "Operation cancelled by user");

        var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(newPath);

        using var tx = new Transaction(doc, "RiveTT: Reload Link From");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        linkType.LoadFrom(modelPath, new WorksetConfiguration());
        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");

        return RiveTTResult<object>.Ok(new { linkId, name = linkInstance.Name, action = "reloaded_from", newPath });
    }

    private static RiveTTResult<object> RemoveLink(Document doc, long linkId, RiveTTSession session)
    {
        if (linkId <= 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "linkId required for remove");

        var element = doc.GetElement(ToolHelpers.ToElementId(linkId));
        if (element == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound, "Link not found");

        var name = element.Name;

        if (!session.RequestConfirmation("remove link", 1, name))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Cancelled, "Operation cancelled by user");

        // Delete the link instance and, if no other instances reference the type, the type too.
        var typeId = element is RevitLinkInstance rli ? rli.GetTypeId() : ElementId.InvalidElementId;

        using var tx = new Transaction(doc, "RiveTT: Remove Link");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        doc.Delete(element.Id);

        bool typeRemoved = false;
        if (typeId != ElementId.InvalidElementId)
        {
            var remainingInstances = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>()
                .Any(i => i.GetTypeId() == typeId);
            if (!remainingInstances && doc.GetElement(typeId) != null)
            {
                doc.Delete(typeId);
                typeRemoved = true;
            }
        }
        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");

        return RiveTTResult<object>.Ok(new { linkId, name, action = "removed", typeRemoved });
    }
}
