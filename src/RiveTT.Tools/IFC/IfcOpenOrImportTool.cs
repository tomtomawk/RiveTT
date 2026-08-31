using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB.IFC;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.IFC;

/// <summary>
/// Opens or imports an IFC file using Application.OpenIFCDocument with IFCImportOptions.
/// Action "open" creates a new Revit document; action "link" creates a reference.
/// </summary>
[ToolSafety(false, true, supportsDryRun: true)]
public class IfcOpenOrImportTool : IRiveTTTool
{
    public string Name => "ifc_open_or_import";
    public string Category => "IFC";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description =>
        "Open or import an IFC file into Revit. Previews by default. Opening an IFC CHANGES THE ACTIVE "
        + "DOCUMENT: every later tool call targets the new document and all caches are flushed. Revit also "
        + "writes a derived .RVT cache next to the IFC. Set dryRun=false to proceed.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var filePath = input["filePath"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(filePath))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "filePath is required",
                suggestion: "Provide the full path to the IFC file");

        // H25-wave: restrict reads to user-owned directories; reject traversal/UNC/system paths.
        // To link an IFC that lives on a network share, use ifc_link instead.
        if (!PathSafety.TryResolveSafe(filePath, out var safePath, out var pathError))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                pathError,
                suggestion: "Provide a path under Documents, Desktop, Downloads, the user profile, or temp");
        filePath = safePath;

        if (!File.Exists(filePath))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"IFC file not found: {filePath}");

        var actionStr = input["action"]?.Value<string>() ?? "open";
        var intentStr = input["intent"]?.Value<string>() ?? "reference";
        var forceImport = input["forceImport"]?.Value<bool>() ?? false;
        var autoJoin = input["autoJoin"]?.Value<bool>() ?? true;

        // No transaction can model this: it opens a document and writes a .RVT cache beside
        // the IFC. The preview reports the resolved intent and the checkable preconditions.
        if (ToolHelpers.GetDryRun(input))
        {
            var cachePath = filePath + ".RVT";
            var blockers = new List<string>();
            if (new FileInfo(filePath).Length == 0)
                blockers.Add($"The IFC file is empty: {filePath}");
            return ChangePreview.Declared(
                $"DryRun: would {actionStr} '{Path.GetFileName(filePath)}' with intent '{intentStr}'."
                + (actionStr.Equals("open", StringComparison.OrdinalIgnoreCase)
                    ? " The ACTIVE DOCUMENT would change to the opened IFC and every cache would be flushed."
                    : ""),
                new
                {
                    action = actionStr,
                    intent = intentStr,
                    filePath,
                    fileSizeBytes = new FileInfo(filePath).Length,
                    derivedRvtCache = cachePath,
                    derivedRvtCacheExists = File.Exists(cachePath),
                    wouldChangeActiveDocument = actionStr.Equals("open", StringComparison.OrdinalIgnoreCase)
                },
                blockers);
        }

        try
        {
            var options = new IFCImportOptions();

            options.Action = actionStr.ToLowerInvariant() switch
            {
                "link" => IFCImportAction.Link,
                _ => IFCImportAction.Open,
            };

            options.Intent = intentStr.ToLowerInvariant() switch
            {
                "parametric" => IFCImportIntent.Parametric,
                _ => IFCImportIntent.Reference,
            };

            options.ForceImport = forceImport;
            options.AutoJoin = autoJoin;

            var app = session.Store.Get<object>("application") as Autodesk.Revit.ApplicationServices.Application;
            if (app == null)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    "Revit Application not available in session");

            var newDoc = app.OpenIFCDocument(filePath, options);

            return RiveTTResult<object>.Ok(new
            {
                action = actionStr,
                intent = intentStr,
                filePath,
                documentTitle = newDoc?.Title ?? "unknown",
                success = newDoc != null,
            });
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"Failed to {actionStr} IFC file: {ex.Message}",
                suggestion: "Ensure the IFC file is valid and Revit supports this operation");
        }
    }
}
