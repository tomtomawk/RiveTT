using System;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Project;

/// <summary>
/// Duplicates a Revit schedule by ID or name with a new name.
/// </summary>
[ToolSafety(false, false, supportsDryRun: true)]
public class DuplicateScheduleTool : IRiveTTTool
{
    public string Name => "duplicate_schedule";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Duplicates a Revit schedule by ID or name with a new name.";
    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var scheduleId = input["scheduleId"]?.Value<long>();
        var scheduleName = input["scheduleName"]?.Value<string>();
        var newName = input["newName"]?.Value<string>();

        if (string.IsNullOrEmpty(newName))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "newName is required");

        try
        {
            ViewSchedule? schedule = null;

            if (scheduleId.HasValue && scheduleId.Value > 0)
            {
                schedule = doc.GetElement(new ElementId(scheduleId.Value)) as ViewSchedule;
            }
            else if (!string.IsNullOrEmpty(scheduleName))
            {
                schedule = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .FirstOrDefault(s => s.Name.Equals(scheduleName, StringComparison.OrdinalIgnoreCase));
            }

            if (schedule == null)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound, "Schedule not found");

            var dryRun = ToolHelpers.GetDryRun(input);
            using var tx = new Transaction(doc, "RiveTT: Duplicate Schedule");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();
            var newId = schedule.Duplicate(ViewDuplicateOption.Duplicate);
            var newSchedule = doc.GetElement(newId) as ViewSchedule;
            if (newSchedule != null)
                newSchedule.Name = newName;
            // Built BEFORE the rollback: afterwards the elements this describes no longer
            // exist and reading a name off one throws. Captured verbatim from the real
            // return, so the preview cannot drift from what applying actually reports.
            var previewPayload = new
            {
                originalName = schedule.Name,
                newName = newSchedule?.Name ?? newName,
                newScheduleId = ToolHelpers.GetElementIdValue(newId)
            };

            if (dryRun)
            {
                ChangePreview.Rollback(tx);
                return ChangePreview.Probed(
                    "DryRun: the operation ran inside a transaction and was rolled back. The model is "
                    + "untouched; what follows is what Revit produced.",
                    previewPayload);
            }

            if (tx.Commit() != TransactionStatus.Committed)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");

return RiveTTResult<object>.Ok(previewPayload);
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}
