using Newtonsoft.Json;

namespace RevitCortex.Core.Telemetry;

/// <summary>
/// One automatic telemetry occurrence (error or bottleneck). Wire schema v1 —
/// see docs/superpowers/specs/2026-07-07-bug-telemetry-pipeline-paid-readiness-design.md.
/// MUST NOT ever carry: tool inputs, raw exception text, document titles/paths,
/// usernames, machine names, parameter/family/type names, element ids.
/// </summary>
public class TelemetryEvent
{
    [JsonProperty("schemaVersion")] public int SchemaVersion { get; set; } = 1;
    [JsonProperty("eventId")] public string EventId { get; set; } = "";
    [JsonProperty("installationId")] public string InstallationId { get; set; } = "";
    [JsonProperty("kind")] public string Kind { get; set; } = "error";
    [JsonProperty("fingerprint")] public string Fingerprint { get; set; } = "";
    [JsonProperty("tool")] public string Tool { get; set; } = "";
    [JsonProperty("errorCode", NullValueHandling = NullValueHandling.Ignore)]
    public string? ErrorCode { get; set; }
    [JsonProperty("failureStage")] public string FailureStage { get; set; } = "tool";
    [JsonProperty("messageClass")] public string MessageClass { get; set; } = "unknown";
    [JsonProperty("messageOrigin")] public string MessageOrigin { get; set; } = "exception";
    [JsonProperty("sanitizedMessage", NullValueHandling = NullValueHandling.Ignore)]
    public string? SanitizedMessage { get; set; }
    [JsonProperty("pluginVersion")] public string PluginVersion { get; set; } = "";
    [JsonProperty("revitVersion")] public string RevitVersion { get; set; } = "";
    [JsonProperty("target")] public string Target { get; set; } = "";
    [JsonProperty("osMajor")] public string OsMajor { get; set; } = "";
    [JsonProperty("locale")] public string Locale { get; set; } = "";
    [JsonProperty("durationMs")] public long DurationMs { get; set; }
    [JsonProperty("responseBytes")] public long ResponseBytes { get; set; }
    [JsonProperty("ts")] public string Timestamp { get; set; } = "";
}
