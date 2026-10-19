using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Jobs;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;

namespace RiveTT.Plugin.Threading;

public class RevitThreadDispatcher
{
    private readonly ToolExecutionHandler _handler;
    private readonly ExternalEvent _externalEvent;
    private readonly object _lock = new object();

    public RevitThreadDispatcher(ToolExecutionHandler handler, ExternalEvent externalEvent)
    {
        _handler = handler;
        _externalEvent = externalEvent;
    }

    public RiveTTResult<object> Execute(IRiveTTTool tool, JObject input, RiveTTSession session,
        int timeoutMs = 120000)
    {
        var jobId = JobRegistry.NewId();
        // H5: only the prepare + Raise pair must be atomic against other requests; the
        // shared ToolExecutionHandler already rejects a concurrent request via
        // TryPrepareExecution. Holding _lock across WaitForCompletion (up to 120s) would
        // serialize *every* tool call behind a single slow/hung operation, so the wait is
        // done OUTSIDE the lock.
        lock (_lock)
        {
            if (!_handler.TryPrepareExecution(tool, input, session, jobId))
            {
                return RiveTTResult<object>.Fail(RiveTTErrorCode.Timeout,
                    $"Tool '{tool.Name}' could not start because a previous Revit event is still pending or running",
                    suggestion: "Wait for Revit to finish the previous operation, then try again. If that operation "
                              + "was a background job (async) or a call that timed out, get_job_status lists it.");
            }

            var raiseResult = _externalEvent.Raise();
            if (raiseResult != ExternalEventRequest.Accepted)
            {
                _handler.ClearPreparedExecution();
                return RiveTTResult<object>.Fail(RiveTTErrorCode.Timeout,
                    $"Revit rejected the event request: {raiseResult}",
                    suggestion: "Revit may be busy with another operation. Try again.");
            }
        }

        if (!_handler.WaitForCompletion(timeoutMs))
            return TimedOut(tool, jobId, timeoutMs);

        return _handler.Result ?? RiveTTResult<object>.Fail(
            RiveTTErrorCode.Unknown, "No result from tool execution");
    }

    /// <summary>
    /// The caller's wait ended. The tool runs on Revit's UI thread and cannot be interrupted, so two things can
    /// be true, and the error says which: it is still running (its outcome is kept for get_job_status), or it
    /// never started (Revit was busy) and is cancelled.
    /// </summary>
    private RiveTTResult<object> TimedOut(IRiveTTTool tool, string jobId, int timeoutMs)
    {
        var running = _handler.IsExecuting(jobId);
        DateTime? since = null;
        if (running)
        {
            since = _handler.RunningSinceUtc(jobId);
            JobRegistry.RegisterRunning(jobId, tool.Name, "after_timeout", since);
            // It may have finished between the end of the wait and this registration.
            if (_handler.TryGetCompleted(jobId, out var done))
            {
                JobRegistry.Complete(jobId, done);
                running = false;
            }
        }

        // H5: clear the prepared/pending state on timeout, otherwise _hasPendingOrRunning
        // stays true and the very next request is refused until Revit eventually fires
        // the deferred event.
        _handler.ClearPreparedExecution();

        var context = new Dictionary<string, object>
        {
            ["stage"] = "timeout",
            ["timeoutMs"] = timeoutMs,
            ["toolStillRunning"] = running,
            ["modelChanged"] = "unknown"
        };
        if (JobRegistry.IsTracked(jobId))
        {
            context["jobId"] = jobId;
            context["getResultWith"] = $"get_job_status(jobId: \"{jobId}\")";
        }

        if (running)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Timeout,
                $"Tool '{tool.Name}' timed out after {timeoutMs}ms but is STILL RUNNING inside Revit.",
                suggestion: $"Do not re-send it: it would run twice. Poll get_job_status(jobId: \"{jobId}\") until it reports "
                          + "completed or failed; its result is kept there. Until then Revit is busy and other tools wait. "
                          + "For a script that long, send it with async: true next time.",
                context: context);

        if (JobRegistry.TryGet(jobId, out var finished) && finished.CompletedUtc != null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Timeout,
                $"Tool '{tool.Name}' timed out after {timeoutMs}ms but FINISHED just after: its result is kept.",
                suggestion: $"Read it with get_job_status(jobId: \"{jobId}\"); verify the model state before retrying.",
                context: context);

        return RiveTTResult<object>.Fail(RiveTTErrorCode.Timeout,
            $"Tool '{tool.Name}' timed out after {timeoutMs}ms WITHOUT having started: Revit was busy with something else.",
            suggestion: "The request was cancelled and will not run. Wait for Revit to be idle (a dialog open, a long "
                      + "regeneration, a previous job: get_job_status lists them), then retry.",
            context: context);
    }

    /// <summary>
    /// Starts <paramref name="tool"/> as a background job: the Revit event is raised and the call returns at once with
    /// a job id. The handler records the outcome in the JobRegistry when the tool finishes.
    /// </summary>
    public RiveTTResult<object> StartJob(IRiveTTTool tool, JObject input, RiveTTSession session)
    {
        var jobId = JobRegistry.NewId();
        lock (_lock)
        {
            if (!_handler.TryPrepareExecution(tool, input, session, jobId))
                return RiveTTResult<object>.Fail(RiveTTErrorCode.Timeout,
                    $"Tool '{tool.Name}' could not start because a previous Revit event is still pending or running",
                    suggestion: "Wait for it to finish; get_job_status lists the background jobs and their state.");

            JobRegistry.RegisterRunning(jobId, tool.Name, "async");
            var raiseResult = _externalEvent.Raise();
            if (raiseResult != ExternalEventRequest.Accepted)
            {
                _handler.ClearPreparedExecution();
                var rejected = RiveTTResult<object>.Fail(RiveTTErrorCode.Timeout,
                    $"Revit rejected the event request: {raiseResult}",
                    suggestion: "Revit may be busy with another operation. Try again.");
                JobRegistry.Complete(jobId, rejected);
                return rejected;
            }
        }

        return RiveTTResult<object>.Ok(new
        {
            jobId,
            status = "running",
            tool = tool.Name,
            message = "Started as a background job. The script keeps running inside Revit; this call did not wait for it.",
            getResultWith = $"get_job_status(jobId: \"{jobId}\")",
            note = "Revit is busy until the job ends: other tools wait or are refused meanwhile. Only get_job_status answers during it."
        });
    }
}
