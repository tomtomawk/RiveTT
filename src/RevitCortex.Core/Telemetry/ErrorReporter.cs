using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RevitCortex.Core.Telemetry;

/// <summary>
/// Single telemetry entry point. Consent-gated (complete no-op when
/// EffectiveEnabled is false — events are not even queued), fail-closed on
/// message text, never throws. One instance per process, wired by the host.
/// </summary>
public class ErrorReporter
{
    private readonly TelemetryConfig _config;
    private readonly TelemetryQueue _queue;
    private readonly TelemetrySender? _sender;
    private readonly TelemetryEnvironment _env;
    private readonly object _countersLock = new object();
    private readonly Dictionary<string, int> _failureCounts = new Dictionary<string, int>();

    // A message is eligible for text transmission only if it is a pure
    // structural template: no ex.Message fingerprints, and no capitalized word
    // in a NON-INITIAL position after stripping. RevitCortex's own template
    // vocabulary is lowercase structural English ("does", "not", "exist",
    // "category", "found"); a mid-sentence Capitalized token that survives
    // stripping is uncontrolled interpolated data (a workset/room/type/family
    // name), which the shape sanitizer would wave through when no punctuation
    // happens to be adjacent. Fail-closed: any doubt -> not a pure template.
    private static readonly Regex RxNonInitialCap = new Regex(
        @"(?<=\S\s)[A-Z][A-Za-z]*", RegexOptions.Compiled);

    /// <summary>Raised once per fingerprint per process when the repeated-failure
    /// threshold is hit. UI is owned by the Plugin layer (Plan 3).</summary>
    public event Action<string, int>? RepeatedFailureDetected;

    public ErrorReporter(TelemetryConfig config, TelemetryQueue queue,
        TelemetrySender? sender, TelemetryEnvironment env)
    {
        _config = config;
        _queue = queue;
        _sender = sender;
        _env = env;
    }

    public void Record(string tool, bool success, string? errorCode, string? message,
        string failureStage, long durationMs, long responseBytes)
    {
        try
        {
            if (!_config.EffectiveEnabled) return;

            if (success)
            {
                if (durationMs < _config.BottleneckDurationMs
                    && responseBytes < _config.BottleneckResponseBytes) return;
                Enqueue(BuildEvent("bottleneck", tool, null, null, failureStage,
                    "unknown", "exception", null, durationMs, responseBytes));
                return;
            }

            var normalized = MessageSanitizer.Normalize(message);
            var messageClass = MessageClassifier.Classify(errorCode, message);
            var fingerprint = ErrorFingerprinter.Compute(
                tool, errorCode, failureStage, messageClass, normalized);

            string origin = "exception";
            string? sanitized = null;
            if (errorCode != null && errorCode != "Unknown"
                && IsPureTemplate(message)
                && MessageSanitizer.TrySanitizeForTransmission(message, out var safe))
            {
                origin = "templated";
                sanitized = safe;
            }

            var evt = BuildEvent("error", tool, errorCode, fingerprint, failureStage,
                messageClass, origin, sanitized, durationMs, responseBytes);
            Enqueue(evt);
            CountFailure(fingerprint);
        }
        catch { /* telemetry must never affect the host */ }
    }

    private static bool IsPureTemplate(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        // Run the sanitizer's own stripping first so quoted names, paths, GUIDs,
        // compound tokens and numbers are already redacted to "_" and do not
        // trip the capital-word check (e.g. 'Strutture' -> _ is fine).
        var stripped = MessageSanitizer.StripForTemplateCheck(message);
        // A non-initial capitalized word surviving stripping = interpolated proper
        // noun / bare name. Reject.
        if (RxNonInitialCap.IsMatch(stripped)) return false;
        return true;
    }

    private TelemetryEvent BuildEvent(string kind, string tool, string? errorCode,
        string? fingerprint, string failureStage, string messageClass, string origin,
        string? sanitized, long durationMs, long responseBytes)
    {
        return new TelemetryEvent
        {
            EventId = Guid.NewGuid().ToString(),
            InstallationId = _config.EnsureInstallationId(),
            Kind = kind,
            Fingerprint = fingerprint ?? ErrorFingerprinter.Compute(
                tool, errorCode, failureStage, messageClass, ""),
            Tool = tool,
            ErrorCode = errorCode,
            FailureStage = failureStage,
            MessageClass = messageClass,
            MessageOrigin = origin,
            SanitizedMessage = sanitized,
            PluginVersion = _env.PluginVersion,
            RevitVersion = _env.RevitVersion,
            Target = _env.Target,
            OsMajor = _env.OsMajor,
            Locale = _env.Locale,
            DurationMs = durationMs,
            ResponseBytes = responseBytes,
            Timestamp = DateTime.UtcNow.ToString("o")
        };
    }

    private void Enqueue(TelemetryEvent evt)
    {
        _queue.Enqueue(evt);
        _sender?.NotifyEnqueued();
    }

    private void CountFailure(string fingerprint)
    {
        int count;
        lock (_countersLock)
        {
            _failureCounts.TryGetValue(fingerprint, out count);
            count++;
            _failureCounts[fingerprint] = count;
        }
        if (count == _config.ZipPromptFailureThreshold)
        {
            try { RepeatedFailureDetected?.Invoke(fingerprint, count); } catch { }
        }
    }
}
