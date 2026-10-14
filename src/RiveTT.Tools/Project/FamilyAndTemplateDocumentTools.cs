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
/// The filePath checks shared by open_family and open_template — required, absolute,
/// correct extension, must exist. Pulled out of both Execute() bodies so this logic can be
/// unit-tested on its own: OpenFamilyTool.Execute/OpenTemplateTool.Execute also reference
/// Autodesk.Revit.UI/DB types further down, and the JIT resolves a method's referenced
/// types eagerly on first call regardless of which branch runs — calling Execute() to
/// reach only these early checks forced a RevitAPIUI load that a standalone `dotnet test`
/// run cannot satisfy (Nice3point.Revit.Api.* ships no real DLL; see AGENTS.md). Calling
/// this method directly needs neither.
/// </summary>
public static class DocumentFilePathValidation
{
    public static RiveTTResult<object>? Validate(string? filePath, string requiredExtension)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "filePath is required and was not provided",
                suggestion: $"Pass filePath as an absolute {requiredExtension} path.");

        if (!Path.IsPathFullyQualified(filePath) ||
            !filePath.EndsWith(requiredExtension, StringComparison.OrdinalIgnoreCase))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"filePath must be an absolute path ending in {requiredExtension} (received: {filePath})");

        // Gated here rather than in each caller: open_family and open_template both come
        // through this helper, and a seventh document tool added later gets the check for
        // free instead of being forgotten the way these two were.
        if (!PathSafety.TryResolveSafe(filePath, out var safePath, out var pathError, allowRevitLibraryRead: true))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, pathError,
                suggestion: "Open the file from the project drive, a share, or a user folder — "
                          + "not from a Windows system folder.");

        if (!File.Exists(safePath))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound, $"File not found: {safePath}");

        return null;
    }
}

/// <summary>
/// Opens a .rfa family file and activates it in Revit, for direct visual editing
/// (as opposed to load_family, which loads a family INTO the active project).
///
/// Document.EditFamily does NOT deadlock from this connector's ExternalEvent
/// dispatcher — a documentation claim that made this tool unreachable, corrected
/// as part of P4.1 in PLAN_CORRECTION.md. All four document-opening paths (a
/// background .rfa, EditFamily on a project family, an activated .rfa, an
/// activated project family) were measured working on 26/08/2026 (see PLAN_CORRECTION.md
/// Annex A).
/// </summary>
[ToolSafety(true, false, supportsDryRun: true)]
public sealed partial class OpenFamilyTool : IRiveTTTool
{
    public string Name => "open_family";
    public string Category => "Documents";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;

    public string Description =>
        "Opens a .rfa family file (filePath), or a family already loaded in a project (familyId/familyName), " +
        "and makes it the active document in Revit, for visual editing (type " +
        "parameters, geometry). The active document CHANGES: every later tool call targets the family until " +
        "you switch back with open_document. The family stays open — call close_document when done, or it " +
        "accumulates for the rest of the session. To load a family INTO the current project instead, use " +
        "load_family; after editing, load_family with action \"load_from_open\" re-injects the open family " +
        "into the project.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var filePath = input["filePath"]?.Value<string>()
                       ?? input["path"]?.Value<string>();
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

        // A family already loaded in a project, instead of a file: familyId/familyName.
        if (string.IsNullOrWhiteSpace(filePath)
            && (input["familyId"] != null || !string.IsNullOrWhiteSpace(input["familyName"]?.Value<string>())))
            return OpenProjectFamily(input, session, dryRun);

        var validationError = DocumentFilePathValidation.Validate(filePath, ".rfa");
        if (validationError != null)
            return validationError;

        var currentDocument = session.Store.Get<object>("activeDocument") as Document;

        if (dryRun)
        {
            return RiveTTResult<object>.Ok(new
            {
                message = $"DryRun: would open and activate '{Path.GetFileName(filePath)}' as a family " +
                          "document. This changes the active document.",
                filePath,
                fileSizeBytes = new FileInfo(filePath!).Length,
                currentDocument = currentDocument?.PathName,
                currentDocumentHasUnsavedChanges = currentDocument?.IsModified ?? false,
                warnings = currentDocument?.IsModified == true
                    ? new[] { "The current document has unsaved changes. Save it first: switching documents does not save it." }
                    : Array.Empty<string>()
            });
        }

        var uiApplication = DocumentLifecycleSupport.ResolveUiApplication(session);
        if (uiApplication == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "No UIApplication in session, so no document can be activated",
                suggestion: "Activate any view in Revit once, then retry.");

        var returnToPath = currentDocument?.PathName;

        using var dialogs = new OpenDialogAutoAnswer(uiApplication);
        try
        {
            uiApplication.OpenAndActivateDocument(filePath);
            var opened = uiApplication.ActiveUIDocument?.Document;

            return RiveTTResult<object>.Ok(new
            {
                message = $"Opened and activated the family '{opened?.Title ?? Path.GetFileName(filePath)}'. " +
                          "The active document has changed — every later tool call targets this family until " +
                          "you switch back." +
                          (dialogs.Answered.Count > 0
                              ? $" {dialogs.Answered.Count} Revit dialog(s) were answered automatically."
                              : ""),
                path = opened?.PathName ?? filePath,
                title = opened?.Title,
                isFamilyDocument = opened?.IsFamilyDocument ?? true,
                returnToPath,
                dismissedDialogs = dialogs.Answered,
                warnings = dialogs.Warnings
            });
        }
        catch (Exception exception)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Could not open and activate the family: {exception.Message}",
                suggestion: "Close any open dialog in Revit and retry.");
        }
    }
}

public sealed partial class OpenFamilyTool
{
    /// <summary>
    /// Opens a family ALREADY LOADED in a project (Document.EditFamily), without a .rfa on disk.
    /// EditFamily returns a document with no path, and Revit can only activate a document by
    /// path, so activation goes through a temporary working copy (SaveAs, then
    /// OpenAndActivateDocument on that path) — the same device open_file uses for .rft. The
    /// project's own family is untouched until load_family(action: "load_from_open").
    /// </summary>
    private static RiveTTResult<object> OpenProjectFamily(JObject input, RiveTTSession session, bool dryRun)
    {
        var application = DocumentLifecycleSupport.ResolveApplication(session);
        if (application == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "No Revit application context is available yet",
                suggestion: "Open Revit (2026.5+ or 2027) and wait for its session to be published.");

        var familyId = input["familyId"]?.Value<long>();
        var familyName = input["familyName"]?.Value<string>();
        var targetName = input["targetDocument"]?.Value<string>();
        var activate = input["activate"]?.Value<bool>() ?? true;

        var uiApplication = DocumentLifecycleSupport.ResolveUiApplication(session);
        var active = uiApplication?.ActiveUIDocument?.Document;
        var projects = Elements.LoadFamilyTool.OpenDocuments(application).Where(d => !d.IsFamilyDocument).ToList();

        Document? target;
        if (!string.IsNullOrWhiteSpace(targetName))
        {
            target = Elements.LoadFamilyTool.MatchByTitle(projects, targetName!);
            if (target == null)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                    $"No open project matches targetDocument '{targetName}'.",
                    suggestion: "Open projects are listed in context.",
                    context: new Dictionary<string, object> { ["openProjects"] = Elements.LoadFamilyTool.DescribeDocs(projects) });
        }
        else if (active != null && !active.IsFamilyDocument) target = active;
        else if (projects.Count == 1) target = projects[0];
        else
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                projects.Count == 0 ? "No project is open: there is no family to open from." :
                "Several projects are open and none is active: targetDocument is required.",
                suggestion: "Pass targetDocument with the title of the project that holds the family.",
                context: new Dictionary<string, object> { ["openProjects"] = Elements.LoadFamilyTool.DescribeDocs(projects) });

        Family? family = familyId is > 0
            ? target.GetElement(new ElementId(familyId.Value)) as Family
            : Elements.LoadFamilyTool.FindFamily(target, familyName ?? "");
        if (family == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                familyId is > 0 ? $"No family with id {familyId} in '{target.Title}'."
                                : $"No family named '{familyName}' in '{target.Title}'.",
                suggestion: "Read family ids and names with load_family(action: \"list\").");
        if (!family.IsEditable)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"Family '{family.Name}' cannot be opened for editing (system family or in-place family).",
                suggestion: "Only a loadable family (.rfa origin) can be opened.");
        if (target.IsModifiable)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"transaction ouverte sur {target.Title}, valider avant ouverture");

        var familyIdValue = ToolHelpers.GetElementIdValue(family.Id);
        if (dryRun)
            return RiveTTResult<object>.Ok(new
            {
                message = $"DryRun: would open the project family '{family.Name}' of '{target.Title}' for editing" +
                          (activate
                              ? " and activate it (a temporary working copy is saved to give it a path; the project's family is untouched until load_family action load_from_open)."
                              : " in the background, without changing the active document."),
                familyId = familyIdValue,
                familyName = family.Name,
                category = family.FamilyCategory?.Name,
                targetDocument = target.Title,
                activate,
                currentDocument = active?.Title,
                warnings = active?.IsModified == true
                    ? new[] { "The current document has unsaved changes. Switching documents does not save it." }
                    : Array.Empty<string>()
            });

        if (activate && uiApplication == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "No UIApplication in session, so no document can be activated",
                suggestion: "Activate any view in Revit once, or pass activate=false.");

        string? workingCopy = null;
        try
        {
            var familyDocument = target.EditFamily(family)
                                 ?? throw new InvalidOperationException("Revit returned no family document.");
            var targetTitle = target.Title;

            if (!activate)
                return RiveTTResult<object>.Ok(new
                {
                    message = $"Opened '{family.Name}' in the background. The active document is unchanged; " +
                              "pass sourceDocument to load_family(action: \"load_from_open\") to load it back.",
                    familyId = familyIdValue,
                    familyName = family.Name,
                    title = familyDocument.Title,
                    targetDocument = targetTitle,
                    activated = false,
                    activeDocument = uiApplication?.ActiveUIDocument?.Document.Title ?? active?.Title,
                    returnToPath = active?.PathName
                });

            using var dialogs = new OpenDialogAutoAnswer(uiApplication!);
            if (string.IsNullOrEmpty(familyDocument.PathName))
            {
                var directory = Path.Combine(Path.GetTempPath(), "RiveTT", "OpenedFamilies", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                var safeName = string.Concat(family.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                workingCopy = Path.Combine(directory, safeName + ".rfa");
                using var saveOptions = new SaveAsOptions { OverwriteExistingFile = false };
                familyDocument.SaveAs(workingCopy, saveOptions);
            }
            uiApplication!.OpenAndActivateDocument(familyDocument.PathName);
            var opened = uiApplication.ActiveUIDocument?.Document;

            return RiveTTResult<object>.Ok(new
            {
                message = $"Opened and activated the project family '{family.Name}'. The active document has " +
                          "changed. Edit it, then load_family(action: \"load_from_open\") re-injects it into the project.",
                familyId = familyIdValue,
                familyName = family.Name,
                title = opened?.Title,
                path = opened?.PathName,
                isFamilyDocument = opened?.IsFamilyDocument ?? true,
                targetDocument = targetTitle,
                activated = true,
                workingCopyPath = workingCopy,
                returnToPath = active?.PathName,
                dismissedDialogs = dialogs.Answered,
                warnings = dialogs.Warnings
            });
        }
        catch (Exception exception)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Could not open the project family: {exception.Message}",
                suggestion: "Close any open dialog in Revit and retry, or pass activate=false.",
                context: new Dictionary<string, object> { ["workingCopyPath"] = workingCopy ?? "" });
        }
    }
}

/// <summary>
/// Opens a .rte template file FOR EDITING — as opposed to create_document, which
/// only reads a template to seed a new project, and never edits the template
/// itself.
/// </summary>
[ToolSafety(true, false, supportsDryRun: true)]
public sealed class OpenTemplateTool : IRiveTTTool
{
    public string Name => "open_template";
    public string Category => "Documents";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;

    public string Description =>
        "Opens a .rte template file and makes it the active document in Revit, to edit the TEMPLATE itself " +
        "(levels, types, view templates that ship with every new project). To start a new PROJECT from a " +
        "template instead, use create_document — that reads the template without touching it. The active " +
        "document changes: every later tool call targets the template until you switch back.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var filePath = input["filePath"]?.Value<string>()
                       ?? input["path"]?.Value<string>();
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

        var validationError = DocumentFilePathValidation.Validate(filePath, ".rte");
        if (validationError != null)
            return validationError;

        var currentDocument = session.Store.Get<object>("activeDocument") as Document;

        if (dryRun)
        {
            return RiveTTResult<object>.Ok(new
            {
                message = $"DryRun: would open and activate '{Path.GetFileName(filePath)}' as an editable " +
                          "template document. This changes the active document.",
                filePath,
                fileSizeBytes = new FileInfo(filePath!).Length,
                currentDocument = currentDocument?.PathName,
                currentDocumentHasUnsavedChanges = currentDocument?.IsModified ?? false,
                warnings = currentDocument?.IsModified == true
                    ? new[] { "The current document has unsaved changes. Save it first: switching documents does not save it." }
                    : Array.Empty<string>()
            });
        }

        var uiApplication = DocumentLifecycleSupport.ResolveUiApplication(session);
        if (uiApplication == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "No UIApplication in session, so no document can be activated",
                suggestion: "Activate any view in Revit once, then retry.");

        var returnToPath = currentDocument?.PathName;

        using var dialogs = new OpenDialogAutoAnswer(uiApplication);
        try
        {
            uiApplication.OpenAndActivateDocument(filePath);
            var opened = uiApplication.ActiveUIDocument?.Document;

            return RiveTTResult<object>.Ok(new
            {
                message = $"Opened and activated the template '{opened?.Title ?? Path.GetFileName(filePath)}'. " +
                          "Changes made here and saved (save_document) modify the template file itself." +
                          (dialogs.Answered.Count > 0
                              ? $" {dialogs.Answered.Count} Revit dialog(s) were answered automatically."
                              : ""),
                path = opened?.PathName ?? filePath,
                title = opened?.Title,
                returnToPath,
                dismissedDialogs = dialogs.Answered,
                warnings = dialogs.Warnings
            });
        }
        catch (Exception exception)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Could not open and activate the template: {exception.Message}",
                suggestion: "Close any open dialog in Revit and retry.");
        }
    }
}

/// <summary>
/// Closes an open document (project, family, or template) — without this,
/// open_family/open_template leave documents open for the rest of the Revit
/// session (measured: three residual family documents by the end of one
/// campaign, PLAN_CORRECTION.md Annex A.5).
///
/// Document.Close(false) throws "The active document may not be closed from the
/// API" when called on the ACTIVE document (measured 27/08/2026, PLAN_CORRECTION.md
/// P1.4 addendum) — this is a real Revit API constraint, not a bug to route
/// around with a background thread. When the target is active, this tool
/// activates another already-open document first (the same two-step sequence
/// that measurement used), then closes the now-inactive target. When the target
/// is the ONLY open document, there is nothing to activate instead, and the tool
/// refuses rather than guess.
/// </summary>
[ToolSafety(false, true, supportsDryRun: true)]
public sealed class CloseDocumentTool : IRiveTTTool
{
    public string Name => "close_document";
    public string Category => "Documents";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;

    public string Description =>
        "Closes an open document (project, family, or template). Defaults to the active document; pass " +
        "filePath to close a different one that is open in the background. saveModified controls whether " +
        "unsaved changes are saved first (default: false, changes are discarded). Closing the ACTIVE document " +
        "requires another open document to switch to first — if none is open, the call is refused rather than " +
        "guessed at.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var application = DocumentLifecycleSupport.ResolveApplication(session);
        if (application == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "No Revit application context is available yet",
                suggestion: "Open Revit (2026.5+ or 2027) and wait for its session to be published.");

        var filePath = input["filePath"]?.Value<string>() ?? input["path"]?.Value<string>();
        var saveModified = input["saveModified"]?.Value<bool>() ?? false;
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

        var uiApplication = DocumentLifecycleSupport.ResolveUiApplication(session);
        var activeDocument = uiApplication?.ActiveUIDocument?.Document;

        Document? target;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            target = activeDocument;
            if (target == null)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    "filePath was not provided and there is no active document to close",
                    suggestion: "Pass filePath to close a specific open document.");
        }
        else
        {
            target = FindOpenDocument(application, filePath!);
            if (target == null)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                    $"No open document matches: {filePath}",
                    suggestion: "The path must match an already-open document exactly. " +
                                "openDocuments in a dryRun call lists what is currently open.");
        }

        var isActive = activeDocument != null && ReferenceEquals(activeDocument, target);
        var swapCandidate = isActive ? FindSwapCandidate(application, target!) : null;

        if (dryRun)
        {
            return RiveTTResult<object>.Ok(new
            {
                message = !isActive
                    ? $"DryRun: would close '{target!.Title}' directly (not the active document)."
                    : swapCandidate != null
                        ? $"DryRun: '{target!.Title}' is the active document — would activate " +
                          $"'{swapCandidate.Title}' first, then close it."
                        : $"DryRun: '{target!.Title}' is the active document and the ONLY open document — " +
                          "cannot close it via the API. Open another document first.",
                path = target.PathName,
                title = target.Title,
                isActive,
                hasUnsavedChanges = target.IsModified,
                saveModified,
                wouldSwapToPath = swapCandidate?.PathName,
                canClose = !isActive || swapCandidate != null,
                openDocuments = DocumentLifecycleSupport.DescribeOpenDocuments(application)
            });
        }

        if (isActive && swapCandidate == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"'{target!.Title}' is the active document and the only one open. Revit's API refuses to " +
                "close the active document directly (The active document may not be closed from the API).",
                suggestion: "Open or activate another document first (open_document, open_family, or " +
                            "open_template), then retry.");

        try
        {
            var closedTitle = target!.Title;
            var closedPath = target.PathName;
            var swappedToPath = (string?)null;

            if (isActive)
            {
                using var dialogs = new OpenDialogAutoAnswer(uiApplication);
                uiApplication!.OpenAndActivateDocument(swapCandidate!.PathName);
                swappedToPath = swapCandidate.PathName;
            }

            var closed = target.Close(saveModified);
            if (!closed)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                    $"Revit refused to close '{closedTitle}' (Document.Close returned false).",
                    suggestion: saveModified
                        ? "Check the document can be saved to its current path."
                        : "The document may have unsaved changes; pass saveModified=true to save first.");

            return RiveTTResult<object>.Ok(new
            {
                message = swappedToPath != null
                    ? $"Activated '{swapCandidate!.Title}', then closed '{closedTitle}'."
                    : $"Closed '{closedTitle}'.",
                closedPath,
                closedTitle,
                savedBeforeClosing = saveModified,
                swappedToPath
            });
        }
        catch (Exception exception)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Could not close the document: {exception.Message}");
        }
    }

    private static Document? FindOpenDocument(Application application, string filePath)
    {
        try
        {
            foreach (Document document in application.Documents)
            {
                if (string.Equals(document.PathName, filePath, StringComparison.OrdinalIgnoreCase))
                    return document;
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Another open document with a real path to reactivate — a document never
    /// saved (empty PathName, e.g. one just opened via EditFamily and not yet
    /// SaveAs'd) cannot be re-activated by path, per PLAN_CORRECTION.md Annex A.2.
    /// </summary>
    private static Document? FindSwapCandidate(Application application, Document excluding)
    {
        try
        {
            foreach (Document document in application.Documents)
            {
                if (ReferenceEquals(document, excluding)) continue;
                if (string.IsNullOrEmpty(document.PathName)) continue;
                return document;
            }
        }
        catch { }
        return null;
    }
}
