using System;
using System.Threading;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Jobs;
using RiveTT.Core.Results;
using RiveTT.Core.Security;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;

namespace RiveTT.Plugin.Threading;

public class ToolExecutionHandler : IExternalEventHandler
{
    private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);
    private readonly object _stateLock = new object();
    private readonly AuditLogger _auditLogger;
    private int _executionId;
    private bool _hasPendingOrRunning;

    public ToolExecutionHandler(AuditLogger? auditLogger = null)
    {
        _auditLogger = auditLogger ?? new AuditLogger();
    }

    // The execution currently inside tool.Execute (started, not finished), so a caller whose wait ended can
    // be told whether the tool is still running in Revit or never started.
    private string? _runningJobId;
    private DateTime _runningSinceUtc;
    private string? _completedJobId;
    private RiveTTResult<object>? _completedResult;

    public IRiveTTTool? PendingTool { get; set; }
    public JObject? PendingInput { get; set; }
    public RiveTTSession? PendingSession { get; set; }
    public string? PendingJobId { get; set; }
    public RiveTTResult<object>? Result { get; private set; }

    /// <summary>True while the execution identified by <paramref name="jobId"/> is inside the tool.</summary>
    public bool IsExecuting(string jobId)
    {
        lock (_stateLock) return _runningJobId == jobId;
    }

    public DateTime? RunningSinceUtc(string jobId)
    {
        lock (_stateLock) return _runningJobId == jobId ? _runningSinceUtc : null;
    }

    /// <summary>The outcome of <paramref name="jobId"/> if it already finished (it can finish between a timeout and its registration).</summary>
    public bool TryGetCompleted(string jobId, out RiveTTResult<object> result)
    {
        lock (_stateLock)
        {
            if (_completedJobId == jobId && _completedResult != null)
            {
                result = _completedResult;
                return true;
            }
        }
        result = null!;
        return false;
    }

    public void Execute(UIApplication app)
    {
        int myId;
        IRiveTTTool? tool;
        JObject? input;
        RiveTTSession? session;
        string? jobId;

        lock (_stateLock)
        {
            myId = _executionId;
            tool = PendingTool;
            input = PendingInput;
            session = PendingSession;
            jobId = PendingJobId;
            if (tool != null && jobId != null)
            {
                _runningJobId = jobId;
                _runningSinceUtc = DateTime.UtcNow;
            }
        }

        var discarded = false;

        try
        {
            if (tool == null || input == null || session == null)
            {
                // Stale Raise: the state was cleared by a timeout and no new request
                // has been prepared. Never touch Result here — overwriting it could
                // clobber the response of a request that just completed but whose
                // dispatcher has not read Result yet.
                return;
            }

            var result = tool.Execute(input, session);
            lock (_stateLock)
            {
                // Only store the result if this execution is still current
                // (not superseded by a timeout + new prepare).
                if (_executionId == myId)
                    Result = result;
                else
                    discarded = true;
                if (jobId != null)
                {
                    _completedJobId = jobId;
                    _completedResult = result;
                }
            }

            // Nobody is waiting any more (async job, or the caller's time limit ended): keep the outcome
            // where get_job_status reads it, and drop what the run may have made stale.
            if (jobId != null && JobRegistry.IsTracked(jobId))
            {
                JobRegistry.Complete(jobId, result);
                session.BumpDocumentVersion();
                session.Cache.InvalidateAll();
                _auditLogger.LogWithPerf(tool.Name, $"job {jobId} finished",
                    result.Success, result.Error?.Code, errorMessage: result.Error?.Message);
            }

            if (discarded)
            {
                // The caller already received Timeout, but the tool ran to completion:
                // the model may differ from what the caller observed. Record the
                // divergence — the audit log is the source of truth.
                _auditLogger.LogWithPerf(tool.Name,
                    "completed_after_timeout (result discarded; model may have changed)",
                    result.Success, result.Error?.Code,
                    errorMessage: result.Error?.Message);
            }
        }
        catch (Exception ex)
        {
            var failure = RiveTTResult<object>.Fail(
                RiveTTErrorCode.Unknown, $"Unhandled exception: {ex.Message}");
            lock (_stateLock)
            {
                if (_executionId == myId)
                    Result = failure;
                if (jobId != null)
                {
                    _completedJobId = jobId;
                    _completedResult = failure;
                }
            }
            if (jobId != null && JobRegistry.IsTracked(jobId))
                JobRegistry.Complete(jobId, failure);
        }
        finally
        {
            lock (_stateLock)
            {
                if (_runningJobId == jobId) _runningJobId = null;
                if (_executionId == myId)
                {
                    PendingTool = null;
                    PendingInput = null;
                    PendingSession = null;
                    PendingJobId = null;
                    _hasPendingOrRunning = false;
                    _resetEvent.Set();
                }
            }
        }
    }

    public bool TryPrepareExecution(IRiveTTTool tool, JObject input, RiveTTSession session, string? jobId = null)
    {
        lock (_stateLock)
        {
            if (_hasPendingOrRunning)
                return false;

            _executionId++;
            PendingTool = tool;
            PendingInput = input;
            PendingSession = session;
            PendingJobId = jobId;
            Result = null;
            _hasPendingOrRunning = true;
            _resetEvent.Reset();
            return true;
        }
    }

    public bool WaitForCompletion(int timeoutMs = 120000)
    {
        return _resetEvent.WaitOne(timeoutMs);
    }

    public void ClearPreparedExecution()
    {
        lock (_stateLock)
        {
            PendingTool = null;
            PendingInput = null;
            PendingSession = null;
            PendingJobId = null;
            _hasPendingOrRunning = false;
            _resetEvent.Set();
        }
    }

    public string GetName() => "RiveTT Tool Execution";
}
