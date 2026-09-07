using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.IFC;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Project;

/// <summary>
/// Navigation only: does not edit/save an existing model. IFC conversion and RFT
/// instantiation use an isolated working directory, never overwrite the source.
/// </summary>
[ToolSafety(true, false, supportsDryRun: true)]
public sealed class OpenFileTool : IRiveTTTool, ICommandTimeoutTool
{
    public string Name => "open_file";
    public string Category => "Documents";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public int CommandTimeoutSeconds => 300;
    public string Description => "Open and activate RVT, RFA, RTE, RFT or IFC in Revit, even while RiveTT is locked. "
        + "RFT creates a new family; IFC converts to a project. Both use a temporary working copy and preserve the source. "
        + "Subsequent calls target the activated document. dryRun previews without opening or creating files.";

    public static bool SupportsExtension(string? extension) => extension?.ToLowerInvariant()
        is ".rvt" or ".rfa" or ".rte" or ".rft" or ".ifc";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var requestedPath = input["filePath"]?.Value<string>() ?? input["path"]?.Value<string>()
            ?? input["targetPath"]?.Value<string>();
        var detach = input["detachFromCentral"]?.Value<bool>() ?? false;
        var dryRun = input["dryRun"]?.Value<bool>() ?? false;
        if (!PathSafety.TryResolveSafe(requestedPath, out var filePath, out var error,
                allowRevitLibraryRead: true))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, error,
                suggestion: "Provide an absolute Revit file path (RVT, RFA, RTE, RFT or IFC).");
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (!SupportsExtension(extension))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"'{extension}' cannot be opened as a Revit document.",
                suggestion: "Use RVT, RFA, RTE, RFT or IFC. DWG, PDF and images require dedicated import/link tools in an existing document.");
        if (detach && extension != ".rvt")
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "detachFromCentral is only applicable to RVT projects.");
        if (!File.Exists(filePath))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound, $"File not found: {filePath}");

        var uiApp = DocumentLifecycleSupport.ResolveUiApplication(session);
        if (uiApp == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "Revit UI application is not available yet.", suggestion: "Wait until Revit finishes starting and retry.");
        var current = uiApp.ActiveUIDocument?.Document;
        if (current?.IsModifiable == true)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "Finish the current Revit transaction before switching documents.");
        if (detach)
        {
            foreach (Document document in uiApp.Application.Documents)
                if (string.Equals(document.PathName, filePath, StringComparison.OrdinalIgnoreCase))
                    return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                        "The requested project is already open; close it before detaching it.");
        }

        var workingCopy = extension is ".ifc" or ".rft";
        var mode = extension == ".ifc" ? "convert_ifc" : extension == ".rft" ? "new_family_from_template" : "open";
        var warnings = new List<string>();
        if (current?.IsModified == true)
            warnings.Add("The previous document has unsaved changes. Switching does not save or close it.");
        if (workingCopy)
            warnings.Add("This opens a generated working copy in the temporary folder. Save As to your project folder to keep it.");
        if (dryRun)
            return RiveTTResult<object>.Ok(new
            {
                dryRun = true, mutated = false, previewMethod = "declared", filePath, mode,
                createsWorkingCopy = workingCopy, detachFromCentral = detach,
                currentDocument = current?.PathName, warnings
            });

        Document? generated = null;
        string? workingDirectory = null;
        using var dialogs = new OpenDialogAutoAnswer(uiApp);
        try
        {
            var activationPath = filePath;
            if (workingCopy)
            {
                workingDirectory = Path.Combine(Path.GetTempPath(), "RiveTT", "OpenedFiles", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workingDirectory);
                if (extension == ".rft")
                    generated = uiApp.Application.NewFamilyDocument(filePath);
                else
                {
                    // Revit's IFC importer can write sidecar caches beside its input.
                    // Keep those effects in the private working directory too.
                    var localIfc = Path.Combine(workingDirectory, Path.GetFileName(filePath));
                    File.Copy(filePath, localIfc, overwrite: false);
                    using var options = new IFCImportOptions
                    {
                        Action = IFCImportAction.Open, Intent = IFCImportIntent.Reference,
                        ForceImport = true, AutoJoin = false
                    };
                    generated = uiApp.Application.OpenIFCDocument(localIfc, options);
                }
                if (generated == null)
                    throw new InvalidOperationException("Revit returned no document.");
                activationPath = Path.Combine(workingDirectory,
                    "Opened-" + Path.GetFileNameWithoutExtension(filePath) + (generated.IsFamilyDocument ? ".rfa" : ".rvt"));
                using var saveOptions = new SaveAsOptions { OverwriteExistingFile = false };
                generated.SaveAs(activationPath, saveOptions);
                if (uiApp.ActiveUIDocument?.Document != generated)
                {
                    if (!generated.Close(false))
                        throw new InvalidOperationException("Revit could not release the generated background document.");
                    generated = null;
                }
            }

            var alreadyActive = string.Equals(uiApp.ActiveUIDocument?.Document.PathName,
                activationPath, StringComparison.OrdinalIgnoreCase);
            if (alreadyActive && detach)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    "The requested project is already active; it cannot be detached by reactivating it.",
                    suggestion: "Close this project first, then open it with detachFromCentral=true.");
            if (!alreadyActive)
            {
                if (detach)
                {
                    using var options = new OpenOptions
                    {
                        DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets
                    };
                    uiApp.OpenAndActivateDocument(ModelPathUtils.ConvertUserVisiblePathToModelPath(activationPath), options, false);
                }
                else uiApp.OpenAndActivateDocument(activationPath);
            }
            var opened = uiApp.ActiveUIDocument?.Document
                ?? throw new InvalidOperationException("Revit did not activate a document.");
            warnings.AddRange(dialogs.Warnings);
            return RiveTTResult<object>.Ok(new
            {
                sourcePath = filePath, path = opened.PathName, title = opened.Title, mode,
                activeDocumentChanged = current != opened, alreadyActive,
                isFamilyDocument = opened.IsFamilyDocument, isWorkshared = opened.IsWorkshared,
                detachedFromCentral = opened.IsDetached,
                createsWorkingCopy = workingCopy, workingDirectory,
                activeViewId = opened.ActiveView?.Id.Value,
                dismissedDialogs = dialogs.Answered, warnings
            });
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"open_file could not open '{Path.GetFileName(filePath)}': {ex.Message}",
                suggestion: "Check the file version and access rights, and finish any operation in Revit before retrying.",
                context: new Dictionary<string, object>
                {
                    ["sourcePath"] = filePath, ["workingDirectory"] = workingDirectory ?? "",
                    ["dismissedDialogs"] = dialogs.Answered
                });
        }
        finally
        {
            if (generated != null && generated.IsValidObject && uiApp.ActiveUIDocument?.Document != generated)
            {
                try { generated.Close(false); } catch { /* Preserve the original error. */ }
            }
        }
    }
}
