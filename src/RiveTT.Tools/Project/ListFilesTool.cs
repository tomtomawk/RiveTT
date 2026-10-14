using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Project;

/// <summary>
/// Lists the files of a folder (name, size, last write), read-only. The script sandbox forbids System.IO,
/// so an exported PDF could not be checked from a script, and batch_export's answer was the only evidence it
/// existed (field report, notices PC). The folder goes through the same PathSafety gate as every tool that
/// touches the disk: user-owned locations only, no traversal, no UNC, no system folders.
/// </summary>
[ToolSafety(true, false)]
public sealed class ListFilesTool : IRiveTTTool
{
    public string Name => "list_files";
    public string Category => "Project";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;

    public string Description =>
        "Lists the files of a folder under the user's Documents, Desktop, Downloads, profile or temp: name, size in bytes " +
        "and last write time (UTC). pattern (default *), recursive (default false), modifiedWithinMinutes to keep only recent " +
        "files, limit (default 200), newest first. Read-only. Use it to check that an export (PDF, DWG, image) was written.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var directory = input["directory"]?.Value<string>();
        var pattern = input["pattern"]?.Value<string>() ?? "*";
        var recursive = input["recursive"]?.Value<bool>() ?? false;
        var within = input["modifiedWithinMinutes"]?.Value<double?>();
        var limit = Math.Clamp(input["limit"]?.Value<int>() ?? 200, 1, 2000);

        if (string.IsNullOrWhiteSpace(directory))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "directory is required",
                suggestion: "The folder to list, e.g. the outputDirectory given to batch_export.");
        if (!PathSafety.TryResolveSafe(directory, out var safeDirectory, out var pathError))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, pathError,
                suggestion: "Give a path under Documents, Desktop, Downloads, the user profile or temp.");
        if (!Directory.Exists(safeDirectory))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound, $"Directory not found: {safeDirectory}");

        try
        {
            var files = new DirectoryInfo(safeDirectory)
                .EnumerateFiles(pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(file => within == null || file.LastWriteTimeUtc >= DateTime.UtcNow.AddMinutes(-within.Value))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToList();

            return RiveTTResult<object>.Ok(new
            {
                directory = safeDirectory,
                pattern,
                recursive,
                totalCount = files.Count,
                returned = Math.Min(files.Count, limit),
                truncated = files.Count > limit,
                files = files.Take(limit).Select(file => new
                {
                    name = file.Name,
                    relativePath = Path.GetRelativePath(safeDirectory, file.FullName),
                    sizeBytes = file.Length,
                    lastWriteUtc = file.LastWriteTimeUtc.ToString("o")
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"list_files could not read the folder: {ex.Message}",
                suggestion: "Check the pattern (wildcards only) and that the folder is readable.");
        }
    }
}
