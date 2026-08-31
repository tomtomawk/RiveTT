using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Project;

/// <summary>
/// Exports a schedule view to CSV/TSV format or returns data as structured JSON.
/// </summary>
// NOT read-only. It reads the model, but it WRITES a file to a path the caller chooses,
// and readOnly:true meant the ribbon write lock never saw it: a locked session could
// still overwrite any file the user can write to. Same defect as
// ifc_set_family_mapping_file, which was reclassified for the same reason. Reading a
// schedule without exportPath is still available -- the lock refuses the whole tool, so
// unlock to export to a file.
[ToolSafety(false, false)]
public class ExportScheduleTool : IRiveTTTool
{
    public string Name => "export_schedule";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Exports a schedule view to CSV/TSV format or returns data as structured JSON.";
    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var scheduleId = input["scheduleId"]?.Value<long>() ?? 0;
        var exportPath = input["exportPath"]?.Value<string>();
        // The MCP surface publishes `format` (csv|tsv|json); only `delimiter` was read,
        // so the published parameter did nothing at all.
        var format = input["format"]?.Value<string>()?.Trim().ToLowerInvariant();
        var delimiter = input["delimiter"]?.Value<string>()
                        ?? format switch
                        {
                            "csv" => "Comma",
                            "tsv" => "Tab",
                            _ => "Tab"
                        };
        var includeHeaders = input["includeHeaders"]?.Value<bool>() ?? true;
        var overwrite = input["overwrite"]?.Value<bool>() ?? false;

        // Resolved BEFORE the schedule is read: a rejected path must fail fast, not after
        // pulling every cell out of the model.
        var safeExportPath = string.Empty;
        if (!string.IsNullOrEmpty(exportPath))
        {
            if (!PathSafety.TryResolveSafe(exportPath, out safeExportPath, out var pathError))
                return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, pathError,
                    suggestion: "Give an absolute path outside the Windows system folders "
                              + "(the project drive and network shares are fine).");

            if (!PathSafety.CanWriteTo(safeExportPath, overwrite, out var overwriteError))
                return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, overwriteError,
                    suggestion: "Replacing a file is a different act from creating one; "
                              + "it has to be asked for.");
        }

        if (scheduleId <= 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "scheduleId is required");

        try
        {
            var schedule = doc.GetElement(new ElementId(scheduleId)) as ViewSchedule;
            if (schedule == null)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound, "Schedule not found");

            var tableData = schedule.GetTableData();
            var sectionData = tableData.GetSectionData(SectionType.Body);
            var headerData = tableData.GetSectionData(SectionType.Header);

            var rows = new List<List<string>>();

            // H3: column headings live in the Header section, not Body row 0. The old
            // code read Body row 0 as the header AND skipped it from the data loop, so
            // every export with includeHeaders=true had wrong column names and lost its
            // first data record. Read the (last) Header row for titles and keep ALL Body
            // rows as data.
            if (includeHeaders && headerData.NumberOfRows > 0)
            {
                var headerRow = new List<string>();
                int headerRowIndex = headerData.NumberOfRows - 1; // bottom-most header row holds the field titles
                for (int col = 0; col < headerData.NumberOfColumns; col++)
                    headerRow.Add(schedule.GetCellText(SectionType.Header, headerRowIndex, col));
                rows.Add(headerRow);
            }

            // Data rows — Body row 0 is real data and must not be skipped.
            for (int row = 0; row < sectionData.NumberOfRows; row++)
            {
                var dataRow = new List<string>();
                for (int col = 0; col < sectionData.NumberOfColumns; col++)
                    dataRow.Add(schedule.GetCellText(SectionType.Body, row, col));
                rows.Add(dataRow);
            }

            // Export to file if path provided
            if (!string.IsNullOrEmpty(exportPath))
            {
                var sep = delimiter switch
                {
                    "Comma" => ",",
                    "Semicolon" => ";",
                    "Space" => " ",
                    _ => "\t"
                };

                var sb = new StringBuilder();
                foreach (var row in rows)
                    sb.AppendLine(string.Join(sep, row));

                File.WriteAllText(safeExportPath, sb.ToString(), Encoding.UTF8);
                return RiveTTResult<object>.Ok(new
                {
                    scheduleName = schedule.Name,
                    exportedTo = safeExportPath,
                    overwroteExistingFile = overwrite,
                    rowCount = rows.Count,
                    columnCount = sectionData.NumberOfColumns
                });
            }

            return RiveTTResult<object>.Ok(new
            {
                scheduleName = schedule.Name,
                rowCount = rows.Count,
                columnCount = sectionData.NumberOfColumns,
                data = rows
            });
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"Could not export the schedule: {ex.Message}",
                suggestion: "Check that the schedule id is a ViewSchedule and that the target "
                          + "folder exists and is writable.");
        }
    }
}
