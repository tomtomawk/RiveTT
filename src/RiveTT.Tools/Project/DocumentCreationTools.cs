using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Project;

/// <summary>
/// Document lifecycle: create a project from a template, and open/activate a
/// project file.
///
/// Why these exist now: the connector documented both as impossible because
/// "UIApplication.OpenAndActivateDocument cannot run inside an API event
/// handler". That restriction is real for API *event* handlers (Idling,
/// DocumentChanged) but not for an ExternalEvent handler, which is precisely
/// the context every RiveTT tool runs in. Autodesk's own guidance (Arnošt
/// Löbel, via The Building Coder) is that switching to an External Event is
/// "both supported and safe" for open-and-activate. Application.NewProjectDocument
/// needs no UI at all: it returns an in-memory document that is saved to disk.
///
/// Consequence for callers: "new project" no longer has to mean
/// save_as_document, which duplicates the open model with all its history.
/// </summary>
internal static class DocumentLifecycleSupport
{
    internal static Application? ResolveApplication(RiveTTSession session)
    {
        if (session.Store.Get<object>("uiApplication") is UIApplication uiApplication)
            return uiApplication.Application;
        return (session.Store.Get<object>("activeDocument") as Document)?.Application;
    }

    internal static UIApplication? ResolveUiApplication(RiveTTSession session)
        => session.Store.Get<object>("uiApplication") as UIApplication;

    internal static bool IsWritableDirectory(string directory)
        => DocumentLifecyclePreview.IsWritableDirectory(directory);

    internal static List<object> DescribeOpenDocuments(Application application)
    {
        var documents = new List<object>();
        try
        {
            foreach (Document document in application.Documents)
            {
                if (document.IsFamilyDocument) continue;
                documents.Add(new
                {
                    title = document.Title,
                    path = document.PathName,
                    isModified = document.IsModified
                });
            }
        }
        catch
        {
            // Enumerating open documents is best-effort context, never the answer.
        }

        return documents;
    }
}

/// <summary>Creates a new project document from a Revit template (.rte).</summary>
[ToolSafety(false, false, supportsDryRun: true)]
public sealed class CreateDocumentTool : IRiveTTTool
{
    public string Name => "create_document";
    public string Category => "Documents";
    // A new project must be creatable even with nothing open.
    public bool RequiresDocument => false;
    public bool IsDynamic => false;

    public string Description =>
        "Creates a NEW empty project from a Revit template (.rte) and saves it to targetPath. This is the " +
        "real 'new project': unlike save_as_document it does not duplicate the open model. Omit templatePath " +
        "to use the Revit default project template. The document is created in memory, saved, then closed; " +
        "set activate=true to open it in Revit afterwards.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var application = DocumentLifecycleSupport.ResolveApplication(session);
        if (application == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "No Revit application context is available yet",
                suggestion: "Open Revit (2026.5+ or 2027) and wait for its session to be published.");

        // Captured up front: creating the document ends with Close(false), which
        // fires DocumentClosing and clears the session store. Resolving the
        // UIApplication after that point found nothing and activation failed on a
        // file that had just been created successfully.
        var uiApplicationAtEntry = DocumentLifecycleSupport.ResolveUiApplication(session);

        var targetPath = input["targetPath"]?.Value<string>()
                         ?? input["filePath"]?.Value<string>()
                         ?? input["path"]?.Value<string>();
        var templatePath = input["templatePath"]?.Value<string>();
        var overwrite = input["overwrite"]?.Value<bool>() ?? false;
        var activate = input["activate"]?.Value<bool>() ?? false;
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

        if (string.IsNullOrWhiteSpace(targetPath))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "targetPath is required and was not provided",
                suggestion: "Pass targetPath as an absolute .rvt path, e.g. " +
                            "{\"targetPath\": \"C:\\\\Projets\\\\T2.rvt\", \"dryRun\": false}.");

        if (!Path.IsPathFullyQualified(targetPath) ||
            !targetPath!.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"targetPath must be an absolute path ending in .rvt (received: {targetPath})");

        if (!PathSafety.TryResolveSafe(targetPath, out var safeTargetPath, out var targetPathError))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, targetPathError,
                suggestion: "Create the project on the project drive or a share, not in a "
                          + "Windows system folder.");
        targetPath = safeTargetPath;

        var defaultTemplate = SafeDefaultTemplate(application);
        // The default template comes from Revit's own configuration and lives under
        // ProgramData — trusted, and deliberately not put through the caller gate. Only a
        // template the CALLER supplied is checked.
        if (!string.IsNullOrWhiteSpace(templatePath))
        {
            if (!PathSafety.TryResolveSafe(templatePath, out var safeTemplatePath, out var templatePathError))
                return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, templatePathError,
                    suggestion: "Point templatePath at a .rte on the project drive, a share, or a user folder.");
            templatePath = safeTemplatePath;
        }

        var resolvedTemplate = string.IsNullOrWhiteSpace(templatePath) ? defaultTemplate : templatePath;

        if (string.IsNullOrWhiteSpace(resolvedTemplate))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "No template available: templatePath was not provided and Revit has no default project template configured.",
                suggestion: "Pass templatePath explicitly, e.g. " +
                            "C:\\ProgramData\\Autodesk\\RVT <year>\\Templates\\French\\Modele-architecture.rte " +
                            "(<year> is 2026 or 2027, matching the running Revit).");

        var targetDirectory = Path.GetDirectoryName(targetPath) ?? "";
        var targetExists = File.Exists(targetPath);

        var blockers = new List<string>();
        if (!File.Exists(resolvedTemplate))
            blockers.Add($"Template not found: {resolvedTemplate}");
        if (targetExists && !overwrite)
            blockers.Add($"Target already exists and overwrite=false: {targetPath}");
        if (!Directory.Exists(targetDirectory))
            blockers.Add($"Target directory does not exist: {targetDirectory}");
        else if (!DocumentLifecycleSupport.IsWritableDirectory(targetDirectory))
            blockers.Add($"Target directory is not writable: {targetDirectory}");

        if (dryRun)
        {
            return RiveTTResult<object>.Ok(new
            {
                message = blockers.Count == 0
                    ? $"DryRun: would create '{Path.GetFileName(targetPath)}' from template " +
                      $"'{Path.GetFileName(resolvedTemplate)}'."
                    : $"DryRun: creation would fail ({blockers.Count} blocker(s)).",
                templatePath = resolvedTemplate,
                templateIsDefault = string.IsNullOrWhiteSpace(templatePath),
                defaultProjectTemplate = defaultTemplate,
                targetPath,
                targetExists,
                overwrite,
                activate,
                blockers,
                openDocuments = DocumentLifecycleSupport.DescribeOpenDocuments(application)
            });
        }

        if (blockers.Count > 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"Cannot create the document: {string.Join("; ", blockers)}");

        Document? created = null;
        try
        {
            created = application.NewProjectDocument(resolvedTemplate);
            if (created == null)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                    $"Revit returned no document for template '{resolvedTemplate}'",
                    suggestion: "The template exists but Revit would not open it: it is usually a template from a newer Revit version, or a corrupted .rte. Try another templatePath, or the default template by omitting the parameter.");

            created.SaveAs(targetPath, new SaveAsOptions { OverwriteExistingFile = overwrite });

            var levelCount = new FilteredElementCollector(created).OfClass(typeof(Level)).GetElementCount();
            var title = created.Title;

            // Close the in-memory copy before activating: the same path cannot be
            // opened twice, and leaving it open would pin the file.
            created.Close(false);
            created = null;

            var activated = false;
            string? activationError = null;
            IReadOnlyList<object> dismissedDialogs = Array.Empty<object>();
            if (activate)
            {
                var uiApplication = uiApplicationAtEntry
                                    ?? DocumentLifecycleSupport.ResolveUiApplication(session);
                if (uiApplication == null)
                {
                    activationError = "No UIApplication in session; the file was created but not opened.";
                }
                else
                {
                    try
                    {
                        using var dialogs = new OpenDialogAutoAnswer(uiApplication);
                        uiApplication.OpenAndActivateDocument(targetPath);
                        activated = true;
                        dismissedDialogs = dialogs.Answered;
                    }
                    catch (Exception exception)
                    {
                        activationError = exception.Message;
                    }
                }
            }

            return RiveTTResult<object>.Ok(new
            {
                message = activated
                    ? $"Created '{title}' from '{Path.GetFileName(resolvedTemplate)}' and activated it in Revit."
                    : $"Created '{title}' from '{Path.GetFileName(resolvedTemplate)}'. " +
                      (activate
                          ? "Activation failed; open it manually in Revit."
                          : "It is NOT open in Revit — open it manually, or call open_document."),
                path = targetPath,
                title,
                templatePath = resolvedTemplate,
                levelCount,
                fileSizeBytes = new FileInfo(targetPath).Length,
                activated,
                activationError,
                dismissedDialogs
            });
        }
        catch (Exception exception)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Could not create the document: {exception.Message}",
                suggestion: "Check that the template path is a valid .rte for the running Revit version and that " +
                            "no other process holds the template or the target file.");
        }
        finally
        {
            // A half-created in-memory document must never be left open: it would
            // hold a file lock for the rest of the Revit session.
            try { created?.Close(false); } catch { }
        }
    }

    private static string? SafeDefaultTemplate(Application application)
    {
        try
        {
            var template = application.DefaultProjectTemplate;
            return string.IsNullOrWhiteSpace(template) ? null : template;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Compatibility alias for the unified Revit file opener.</summary>
[ToolSafety(true, false, supportsDryRun: true)]
public sealed class OpenDocumentTool : IRiveTTTool, ICommandTimeoutTool
{
    public int CommandTimeoutSeconds => 300;
    public string Name => "open_document";
    public string Category => "Documents";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "Compatibility alias for open_file: opens and activates RVT, RFA, RTE, RFT or IFC, also while RiveTT is locked.";
    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var request = (JObject)input.DeepClone();
        request["dryRun"] = input["dryRun"]?.Value<bool>() ?? true;
        request["detachFromCentral"] = input["detachFromCentral"]?.Value<bool>() ?? false;
        return new OpenFileTool().Execute(request, session);
    }
}
