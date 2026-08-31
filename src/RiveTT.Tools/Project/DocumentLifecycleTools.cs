using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Project;

/// <summary>
/// Shared preview logic for the two lifecycle writes. They are the most expensive
/// and least reversible operations the connector exposes (a 200 MB Save As takes
/// ~13 s), and they used to be the only writes with no dryRun at all — the global
/// contract advertises dryRunDefault=true.
/// </summary>
internal static class DocumentLifecyclePreview
{
    internal static bool IsWritableDirectory(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return false;
            var probe = Path.Combine(directory, $".rivett-probe-{Guid.NewGuid():N}.tmp");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsFileLocked(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static long? FileSizeBytes(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : (long?)null; }
        catch { return null; }
    }
}

[ToolSafety(false, false, supportsDryRun: true)]
public sealed class SaveDocumentTool : IRiveTTTool
{
    public string Name => "save_document";
    public string Category => "Documents";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public string Description =>
        "Saves the active Revit project at its current path. Supports dryRun: the preview reports the " +
        "target path, whether the document has unsaved changes and any predictable blocker, without saving.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var document = session.Store.Get<object>("activeDocument") as Document;
        if (document == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");
        if (string.IsNullOrWhiteSpace(document.PathName))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "This project has no path yet", suggestion: "Use save_as_document with an absolute RVT path.");

        var dryRun = input["dryRun"]?.Value<bool>() ?? false;
        var path = document.PathName;

        if (dryRun)
        {
            var blockers = new List<string>();
            var directory = Path.GetDirectoryName(path) ?? "";
            if (!Directory.Exists(directory)) blockers.Add($"Directory does not exist: {directory}");
            else if (!DocumentLifecyclePreview.IsWritableDirectory(directory))
                blockers.Add($"Directory is not writable: {directory}");
            if (DocumentLifecyclePreview.IsFileLocked(path))
                blockers.Add("The target file is locked by another process.");
            if (document.IsReadOnly) blockers.Add("Revit reports this document as read-only.");

            return RiveTTResult<object>.Ok(new
            {
                message = blockers.Count == 0
                    ? $"DryRun: would save '{document.Title}' to its current path."
                    : $"DryRun: save would likely fail ({blockers.Count} blocker(s)).",
                path,
                title = document.Title,
                hasUnsavedChanges = document.IsModified,
                currentFileSizeBytes = DocumentLifecyclePreview.FileSizeBytes(path),
                isWorkshared = document.IsWorkshared,
                isReadOnly = document.IsReadOnly,
                blockers
            });
        }

        try
        {
            document.Save();
            return RiveTTResult<object>.Ok(new
            {
                path = document.PathName,
                title = document.Title,
                fileSizeBytes = DocumentLifecyclePreview.FileSizeBytes(document.PathName)
            });
        }
        catch (Exception exception)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Could not save document: {exception.Message}");
        }
    }
}

[ToolSafety(false, false, supportsDryRun: true)]
public sealed class SaveAsDocumentTool : IRiveTTTool
{
    public string Name => "save_as_document";
    public string Category => "Documents";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public string Description =>
        "Saves the active Revit project to an absolute RVT path (parameter: targetPath). Supports dryRun: " +
        "the preview reports source path, target path, whether the target exists, the overwrite policy and " +
        "predictable blockers, without writing. This duplicates the OPEN document — it does not create a " +
        "blank project from a template.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var document = session.Store.Get<object>("activeDocument") as Document;
        // filePath / path are accepted aliases: a wrong parameter name used to reach
        // the implementation as "no target at all" and surfaced as an opaque failure.
        var targetPath = input["targetPath"]?.Value<string>()
                         ?? input["filePath"]?.Value<string>()
                         ?? input["path"]?.Value<string>();
        var overwrite = input["overwrite"]?.Value<bool>() ?? false;
        var dryRun = input["dryRun"]?.Value<bool>() ?? false;

        if (document == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        if (string.IsNullOrWhiteSpace(targetPath))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "targetPath is required and was not provided",
                suggestion: "Pass targetPath (aliases: filePath, path) as an absolute .rvt path, " +
                            "e.g. {\"targetPath\": \"C:\\\\Projets\\\\model_V4.rvt\", \"overwrite\": false}.");

        if (!Path.IsPathFullyQualified(targetPath) ||
            !targetPath!.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"targetPath must be an absolute path ending in .rvt (received: {targetPath})");

        if (!PathSafety.TryResolveSafe(targetPath, out var safeTargetPath, out var pathError))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, pathError,
                suggestion: "Save to the project drive or a share, not into a Windows "
                          + "system folder.");
        targetPath = safeTargetPath;

        var targetDirectory = Path.GetDirectoryName(targetPath) ?? "";
        var targetExists = File.Exists(targetPath);

        if (dryRun)
        {
            var blockers = new List<string>();
            if (targetExists && !overwrite)
                blockers.Add($"Target already exists and overwrite=false: {targetPath}");
            if (!Directory.Exists(targetDirectory))
                blockers.Add($"Target directory does not exist: {targetDirectory}");
            else if (!DocumentLifecyclePreview.IsWritableDirectory(targetDirectory))
                blockers.Add($"Target directory is not writable: {targetDirectory}");
            if (targetExists && DocumentLifecyclePreview.IsFileLocked(targetPath))
                blockers.Add("The target file is locked by another process.");

            return RiveTTResult<object>.Ok(new
            {
                message = blockers.Count == 0
                    ? $"DryRun: would save '{document.Title}' as '{Path.GetFileName(targetPath)}'."
                    : $"DryRun: save-as would fail ({blockers.Count} blocker(s)).",
                sourcePath = document.PathName,
                targetPath,
                targetExists,
                overwrite,
                sourceFileSizeBytes = DocumentLifecyclePreview.FileSizeBytes(document.PathName),
                hasUnsavedChanges = document.IsModified,
                isWorkshared = document.IsWorkshared,
                blockers,
                note = "Save As duplicates the currently open document, including everything already in it. " +
                       "There is no tool to create a blank project from a template."
            });
        }

        if (targetExists && !overwrite)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"Target already exists: {targetPath}", suggestion: "Set overwrite=true to replace it.");

        if (!Directory.Exists(targetDirectory))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"Target directory does not exist: {targetDirectory}",
                suggestion: "Create the directory first, or pick an existing one.");

        try
        {
            document.SaveAs(targetPath, new SaveAsOptions { OverwriteExistingFile = overwrite });
            return RiveTTResult<object>.Ok(new
            {
                path = targetPath,
                title = document.Title,
                fileSizeBytes = DocumentLifecyclePreview.FileSizeBytes(targetPath),
                // The session's caches are flushed on this event; say so, because the
                // previous behavior (stale reads afterwards) burned several sessions.
                cachesInvalidated = true
            });
        }
        catch (Exception exception)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Could not save project as: {exception.Message}");
        }
    }
}
