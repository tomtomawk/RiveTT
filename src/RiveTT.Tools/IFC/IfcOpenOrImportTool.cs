using System;
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
[ToolSafety(false, true)]
public class IfcOpenOrImportTool : IRiveTTTool
{
    public string Name => "ifc_open_or_import";
    public string Category => "IFC";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "Open or import an IFC file into Revit";

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

        if (!session.RequestConfirmation($"{actionStr} IFC file", 1, $"File: {Path.GetFileName(filePath)}"))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Cancelled, "Operation cancelled by user");

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
