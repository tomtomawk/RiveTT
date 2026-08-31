using System;
using System.Collections.Generic;
using System.IO;

namespace RiveTT.Tools.Utilities;

/// <summary>
/// Validates caller-supplied file paths before read or write.
///
/// The rule is a DENY list of places nobody's project lives, not an allow list of the
/// current user's personal folders. The allow-list version refused
/// <c>P:\Projets\2026-047\...</c> — the agency's own project drive, where every export
/// is actually meant to land — while <c>save_as_document</c> wrote a .rvt anywhere at
/// all because it never called this at the time. It cost the daily gesture and did not
/// close the door it named. A control that blocks normal work gets bypassed, not
/// respected.
///
/// What stays refused: Windows, Program Files, ProgramData, and the RiveTT install
/// itself. Traversal (<c>..</c>) is collapsed first, so it cannot be used to reach them.
/// </summary>
public static class PathSafety
{
    /// <summary>
    /// Canonical, trailing-separator-terminated roots no caller-supplied path may resolve
    /// under. System locations and RiveTT's own state: writing there is either a mistake
    /// or an attempt, never a project deliverable.
    /// </summary>
    private static readonly Lazy<string[]> DeniedRoots = new(BuildDeniedRoots);

    private static string[] BuildDeniedRoots()
    {
        var roots = new List<string>();

        void Add(Environment.SpecialFolder folder)
        {
            try
            {
                var p = Environment.GetFolderPath(folder);
                if (!string.IsNullOrEmpty(p)) roots.Add(WithSeparator(Path.GetFullPath(p)));
            }
            catch { /* folder unavailable on this OS/profile */ }
        }

        Add(Environment.SpecialFolder.Windows);
        Add(Environment.SpecialFolder.System);
        Add(Environment.SpecialFolder.SystemX86);
        Add(Environment.SpecialFolder.ProgramFiles);
        Add(Environment.SpecialFolder.ProgramFilesX86);
        Add(Environment.SpecialFolder.CommonApplicationData);

        // RiveTT's own state: the audit log is evidence, and a tool that could overwrite
        // it could erase the trace of what it did. The scripts folder is executed code.
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
                roots.Add(WithSeparator(Path.GetFullPath(Path.Combine(localAppData, "RiveTT"))));
        }
        catch { }

        return roots.ToArray();
    }

    private static string WithSeparator(string path)
    {
        if (!path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            path += Path.DirectorySeparatorChar;
        return path;
    }

    /// <summary>
    /// Resolves <paramref name="userPath"/> to an absolute canonical path and verifies it
    /// does not land in a system location. Local drives, mapped drives (P:\) and UNC
    /// shares are all accepted: that is where an agency's projects live.
    /// </summary>
    /// <param name="userPath">The raw caller-supplied path.</param>
    /// <param name="resolvedPath">The canonical absolute path, when valid.</param>
    /// <param name="error">A human-readable reason when invalid.</param>
    /// <param name="allowUnc">Kept for source compatibility with the tools that pass it.
    /// UNC is accepted either way now; the parameter no longer changes the outcome and
    /// new code should not pass it.</param>
    public static bool TryResolveSafe(string? userPath, out string resolvedPath, out string error,
        bool allowUnc = false)
    {
        _ = allowUnc;
        resolvedPath = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(userPath))
        {
            error = "No file path provided.";
            return false;
        }

        // Tested on the RAW input: GetFullPath roots everything it returns, resolving a
        // relative path against the process working directory — which, inside Revit, is
        // the Revit install folder. Nothing a caller writes means that.
        if (!Path.IsPathRooted(userPath))
        {
            error = "Path must be absolute (a drive letter, or a UNC share).";
            return false;
        }

        string full;
        try
        {
            // GetFullPath collapses ".." and normalizes separators. Doing this BEFORE the
            // deny check is what makes traversal ineffective rather than merely detected.
            full = Path.GetFullPath(userPath);
        }
        catch (Exception ex)
        {
            error = $"Invalid path: {ex.Message}";
            return false;
        }

        foreach (var root in DeniedRoots.Value)
        {
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                error = "Path is inside a Windows system directory or RiveTT's own state " +
                        "(Windows, Program Files, ProgramData, %LOCALAPPDATA%\\RiveTT).";
                return false;
            }
        }

        resolvedPath = full;
        return true;
    }

    /// <summary>
    /// Guards the difference between creating a file and destroying one.
    ///
    /// Overwriting is not the same act as writing, and an export tool that silently
    /// replaced an existing file gave the caller no way to notice it had. Every tool
    /// writing to a caller-supplied path calls this after
    /// <see cref="TryResolveSafe"/>: it refuses an existing target unless the caller
    /// asked for the replacement in so many words.
    /// </summary>
    /// <param name="resolvedPath">The canonical path, already validated.</param>
    /// <param name="overwrite">The caller's explicit <c>overwrite</c> flag.</param>
    /// <param name="error">Set when the write must be refused.</param>
    public static bool CanWriteTo(string resolvedPath, bool overwrite, out string error)
    {
        error = string.Empty;
        if (overwrite || !File.Exists(resolvedPath)) return true;

        error = $"'{Path.GetFileName(resolvedPath)}' already exists in " +
                $"'{Path.GetDirectoryName(resolvedPath)}'. Pass overwrite=true to replace it, " +
                "or choose another name.";
        return false;
    }
}
