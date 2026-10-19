using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using RiveTT.Core.Results;

namespace RiveTT.Core.Jobs;

/// <summary>One tracked execution: started by <c>async: true</c>, or a call that outlived its time limit.</summary>
public sealed class JobRecord
{
    public string Id { get; init; } = "";
    public string Tool { get; init; } = "";
    /// <summary>"async" (requested) or "after_timeout" (the caller's wait ended, the tool kept running).</summary>
    public string Origin { get; init; } = "async";
    public DateTime StartedUtc { get; init; }
    public DateTime? CompletedUtc { get; set; }
    public RiveTTResult<object>? Result { get; set; }

    public string Status => CompletedUtc == null ? "running" : Result is { Success: true } ? "completed" : "failed";
}

/// <summary>
/// Results of executions nobody was waiting for any more. A tool runs on Revit's UI thread and cannot be
/// interrupted: when its caller stops waiting (timeout) or never waited (async), the outcome used to be
/// discarded and left only a line in the audit log. It is kept here instead, readable with get_job_status.
/// In memory, per Revit session; bounded so a long session cannot grow it without limit.
/// </summary>
public static class JobRegistry
{
    private const int MaxJobs = 50;
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);
    private static readonly ConcurrentDictionary<string, JobRecord> Jobs = new();

    public static string NewId() => "job_" + Guid.NewGuid().ToString("N").Substring(0, 12);

    /// <summary>Starts tracking a job. Keeps the existing record if the id is already known (it completed first).</summary>
    public static JobRecord RegisterRunning(string id, string tool, string origin, DateTime? startedUtc = null)
    {
        Prune();
        return Jobs.GetOrAdd(id, _ => new JobRecord
        {
            Id = id, Tool = tool, Origin = origin, StartedUtc = startedUtc ?? DateTime.UtcNow
        });
    }

    public static bool IsTracked(string? id) => id != null && Jobs.ContainsKey(id);

    public static void Complete(string id, RiveTTResult<object> result)
    {
        if (!Jobs.TryGetValue(id, out var job)) return;
        job.Result = result;
        job.CompletedUtc = DateTime.UtcNow;
    }

    public static bool TryGet(string id, out JobRecord job) => Jobs.TryGetValue(id, out job!);

    public static IReadOnlyList<JobRecord> All() => Jobs.Values.OrderByDescending(j => j.StartedUtc).ToList();

    public static void Clear() => Jobs.Clear();

    private static void Prune()
    {
        var cutoff = DateTime.UtcNow - MaxAge;
        foreach (var old in Jobs.Values.Where(j => j.CompletedUtc != null && j.CompletedUtc < cutoff).ToList())
            Jobs.TryRemove(old.Id, out _);
        if (Jobs.Count < MaxJobs) return;
        foreach (var surplus in Jobs.Values.Where(j => j.CompletedUtc != null)
                     .OrderBy(j => j.CompletedUtc).Take(Jobs.Count - MaxJobs + 1).ToList())
            Jobs.TryRemove(surplus.Id, out _);
    }
}
