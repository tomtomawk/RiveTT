using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RiveTT.Core.Results;
using Newtonsoft.Json.Linq;

namespace RiveTT.Tools.Utilities;

/// <summary>
/// Centralized failure handling for tool transactions. Without a preprocessor,
/// any Revit warning raised during Commit() opens a modal TaskDialog on the UI
/// thread, freezing the MCP bridge until a human clicks it. SuppressWarnings
/// installs a preprocessor that deletes warnings and rolls the transaction back
/// on errors — both without UI. After Commit(), callers must check the returned
/// TransactionStatus: a rolled-back commit surfaces the captured errors as a
/// structured failure instead of a silent success.
/// </summary>
public static class TransactionFailureHandling
{
    /// <summary>
    /// Installs the warning-suppressing preprocessor on the transaction.
    /// Call AFTER Start(): Revit resets all failure options when a transaction starts.
    /// Returns the capture object: after a Commit() that does not return
    /// TransactionStatus.Committed, <see cref="FailureCapture.Errors"/> holds
    /// the Revit error descriptions for the Fail message.
    /// </summary>
    public static FailureCapture SuppressWarnings(Transaction tx)
        => SuppressWarnings(tx, null);

    public static FailureCapture SuppressWarnings(Transaction tx,
        ISet<string>? allowedWarningIds)
    {
        if (tx.GetStatus() != TransactionStatus.Started)
            throw new System.InvalidOperationException(
                "Start the transaction before installing failure handling; Start resets the options.");
        var capture = new FailureCapture(allowedWarningIds);
        var options = tx.GetFailureHandlingOptions();
        options.SetFailuresPreprocessor(capture);
        options.SetClearAfterRollback(true);
        tx.SetFailureHandlingOptions(options);
        return capture;
    }

    public static FailureCapture FromInput(Transaction tx, JObject input)
    {
        var policy = (input["warningPolicy"]?.Value<string>() ?? "suppress_all").ToLowerInvariant();
        if (policy == "suppress_all") return SuppressWarnings(tx);
        if (policy != "allow_list")
            throw new System.ArgumentException("warningPolicy must be suppress_all or allow_list");
        var allowed = input["allowedWarningIds"]?.ToObject<HashSet<string>>()
            ?? new HashSet<string>();
        return SuppressWarnings(tx, allowed);
    }

    /// <summary>
    /// Compact "; "-joined summary of captured errors for Fail messages. Identical
    /// messages are counted, not repeated: 77 copies of "cannot cut instance out of wall"
    /// used to read as three of them and "(+74 more)", which hid that it was ONE cause.
    /// </summary>
    public static string Describe(FailureCapture capture)
    {
        if (capture.Errors.Count == 0)
            return "Revit rolled back the transaction (no error description available)";
        var groups = capture.ErrorGroups;
        var take = groups.Count > 3 ? 3 : groups.Count;
        var head = string.Join("; ", groups.Take(take).Select(group =>
            group.Count > 1 ? $"{group.Message} (x{group.Count})" : group.Message));
        return groups.Count > take ? head + $"; (+{groups.Count - take} other message(s))" : head;
    }

    public static RiveTTResult<object> ToFailure(
        FailureCapture capture, string message, string repairHint)
    {
        return RiveTTResult<object>.Fail(
            RiveTTErrorCode.TransactionFailed,
            $"{message}: {Describe(capture)}",
            suggestion: repairHint,
            context: ToContext(capture, repairHint));
    }

    /// <summary>
    /// The structured context of a rolled-back commit, shared by <see cref="ToFailure"/>
    /// and callers that build their own message (send_code_to_revit). errorGroups carries
    /// the ids PER MESSAGE: a flat failedElementIds cannot say which element raised which
    /// error once two different failures are mixed.
    /// </summary>
    public static Dictionary<string, object> ToContext(FailureCapture capture, string? repairHint)
    {
        return new Dictionary<string, object>
        {
            ["warnings"] = capture.Warnings.ToArray(),
            ["errors"] = capture.Errors.ToArray(),
            ["errorGroups"] = capture.ErrorGroups.Select(group => (object)new
            {
                message = group.Message,
                count = group.Count,
                elementIds = group.ElementIds.Take(50).ToArray(),
                elementIdsTruncated = group.ElementIds.Count > 50
            }).ToArray(),
            ["rolledBack"] = true,
            ["failedElementIds"] = capture.FailedElementIds.OrderBy(id => id).ToArray(),
            ["repairHints"] = string.IsNullOrWhiteSpace(repairHint) ? Array.Empty<string>() : new[] { repairHint! },
            ["warningsSuppressed"] = capture.WarningsSuppressed
        };
    }

    /// <summary>One distinct Revit error text, how often it was raised, and the elements it named.</summary>
    public sealed class FailureGroup
    {
        public FailureGroup(string message) => Message = message;
        public string Message { get; }
        public int Count { get; internal set; }
        public List<long> ElementIds { get; } = new List<long>();
    }

    public sealed class FailureCapture : IFailuresPreprocessor
    {
        private readonly ISet<string>? _allowedWarningIds;
        public FailureCapture(ISet<string>? allowedWarningIds = null)
        {
            _allowedWarningIds = allowedWarningIds;
        }

        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public HashSet<long> FailedElementIds { get; } = new HashSet<long>();
        public int WarningsSuppressed { get; private set; }

        private readonly List<FailureGroup> _errorGroups = new List<FailureGroup>();

        /// <summary>Errors grouped by text, in order of first appearance, with their element ids.</summary>
        public IReadOnlyList<FailureGroup> ErrorGroups => _errorGroups;

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            var hasError = false;
            foreach (var failure in failuresAccessor.GetFailureMessages())
            {
                var severity = failure.GetSeverity();
                if (severity == FailureSeverity.Warning)
                {
                    Warnings.Add(failure.GetDescriptionText());
                    var ids = CaptureIds(failure);
                    var failureId = failure.GetFailureDefinitionId().Guid.ToString("D");
                    if (_allowedWarningIds == null || _allowedWarningIds.Contains(failureId))
                    {
                        WarningsSuppressed++;
                        failuresAccessor.DeleteWarning(failure);
                    }
                    else
                    {
                        hasError = true;
                        RecordError($"Unapproved warning {failureId}: {failure.GetDescriptionText()}", ids);
                    }
                }
                else if (severity == FailureSeverity.Error
                         || severity == FailureSeverity.DocumentCorruption)
                {
                    hasError = true;
                    RecordError(failure.GetDescriptionText(), CaptureIds(failure));
                }
            }

            return hasError
                ? FailureProcessingResult.ProceedWithRollBack
                : FailureProcessingResult.Continue;
        }

        /// <summary>
        /// Records one error. Public so a caller that meets a failure outside Revit's
        /// failure processing (an exception inside a script section) can report it in the
        /// same shape.
        /// </summary>
        public void RecordError(string message, IEnumerable<long>? elementIds = null)
        {
            Errors.Add(message);
            var group = _errorGroups.FirstOrDefault(existing => existing.Message == message);
            if (group == null)
            {
                group = new FailureGroup(message);
                _errorGroups.Add(group);
            }
            group.Count++;
            if (elementIds == null) return;
            foreach (var id in elementIds)
                if (!group.ElementIds.Contains(id)) group.ElementIds.Add(id);
        }

        private List<long> CaptureIds(FailureMessageAccessor failure)
        {
            var ids = new List<long>();
            foreach (var id in failure.GetFailingElementIds())
                ids.Add(ToolHelpers.GetElementIdValue(id));
            foreach (var id in failure.GetAdditionalElementIds())
                ids.Add(ToolHelpers.GetElementIdValue(id));
            FailedElementIds.UnionWith(ids);
            return ids;
        }
    }
}
