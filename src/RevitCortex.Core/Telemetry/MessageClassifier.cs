namespace RevitCortex.Core.Telemetry;

/// <summary>
/// Maps a failure (error code + raw local message) to a coarse class so
/// telemetry stays useful without transmitting raw message text. May inspect
/// the raw local message; the raw text is then discarded (never transmitted
/// from here). See docs/superpowers/specs/2026-07-07-bug-telemetry-pipeline-paid-readiness-design.md
/// ("Message classes") for the fixed vocabulary this returns.
/// </summary>
public static class MessageClassifier
{
    public static string Classify(string? errorCode, string? message)
    {
        var m = (message ?? "").ToLowerInvariant();

        if (m.Contains("unhandled exception")) return "exception";

        switch (errorCode)
        {
            case "Timeout": return "timeout";
            case "Cancelled": return "cancelled";
            case "TransactionFailed": return "transaction_failed";
            case "PermissionDenied":
                return m.Contains("read-only") ? "read_only_block" : "permission_denied";
            case "Unknown": return "exception";
        }

        if (m.Contains("parameter") &&
            (m.Contains("not found") || m.Contains("missing") || m.Contains("does not exist")))
            return "parameter_missing";
        if (m.Contains("category")) return "invalid_category";
        if (m.Contains("parse") || m.Contains("json") || m.Contains("deserial")) return "parse_error";
        if (m.Contains("socket") || m.Contains("connect") || m.Contains("bridge")) return "connection_failed";

        return "unknown";
    }
}
