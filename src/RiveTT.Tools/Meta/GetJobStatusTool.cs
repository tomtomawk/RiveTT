using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Jobs;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;

namespace RiveTT.Tools.Meta;

/// <summary>
/// State and result of a background job (send_code_to_revit with async: true) or of a call whose time limit
/// ended while the tool kept running in Revit. Answered on the calling thread, not through the Revit event:
/// the event is busy running the very job being asked about.
/// </summary>
[ToolSafety(true, false)]
public sealed class GetJobStatusTool : IRiveTTTool, IInlineTool
{
    public string Name => "get_job_status";
    public string Category => "Meta";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description =>
        "Reports a background job started with send_code_to_revit async:true, or a call that timed out while still running in Revit: "
        + "status (running | completed | failed), seconds elapsed, then the tool's own result. Without jobId, lists the known jobs. "
        + "Answers even while Revit is busy running the job.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var jobId = input["jobId"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(jobId))
            return RiveTTResult<object>.Ok(new
            {
                jobCount = JobRegistry.All().Count,
                jobs = JobRegistry.All().Select(Summarize).ToList()
            });

        if (!JobRegistry.TryGet(jobId!.Trim(), out var job))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                $"No job '{jobId}'.",
                suggestion: "Jobs live in memory for this Revit session only (kept 6 h, 50 at most). Known: "
                          + (JobRegistry.All().Count == 0 ? "none" : string.Join(", ", JobRegistry.All().Select(j => j.Id))) + ".",
                context: new Dictionary<string, object> { ["modelChanged"] = false });

        var elapsed = Math.Round(((job.CompletedUtc ?? DateTime.UtcNow) - job.StartedUtc).TotalSeconds, 1);
        if (job.CompletedUtc == null)
            return RiveTTResult<object>.Ok(new
            {
                jobId = job.Id,
                tool = job.Tool,
                origin = job.Origin,
                status = "running",
                startedUtc = job.StartedUtc.ToString("o"),
                elapsedSeconds = elapsed,
                message = "Still running inside Revit. Ask again in a few seconds; do not re-send the script."
            });

        var result = job.Result;
        return RiveTTResult<object>.Ok(new
        {
            jobId = job.Id,
            tool = job.Tool,
            origin = job.Origin,
            status = job.Status,
            startedUtc = job.StartedUtc.ToString("o"),
            completedUtc = job.CompletedUtc?.ToString("o"),
            elapsedSeconds = elapsed,
            result = result is { Success: true, Data: not null } ? JToken.FromObject(result.Data) : null,
            error = result?.Error == null ? null : JToken.FromObject(result.Error)
        });
    }

    private static object Summarize(JobRecord job) => new
    {
        jobId = job.Id,
        tool = job.Tool,
        origin = job.Origin,
        status = job.Status,
        startedUtc = job.StartedUtc.ToString("o"),
        elapsedSeconds = Math.Round(((job.CompletedUtc ?? DateTime.UtcNow) - job.StartedUtc).TotalSeconds, 1)
    };
}
