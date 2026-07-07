using Newtonsoft.Json;

namespace RevitCortex.Core.Telemetry;

/// <summary>Exact known-issue match returned by the ingest Worker for a submitted fingerprint.</summary>
public class KnownIssueMatch
{
    [JsonProperty("fingerprint")] public string Fingerprint { get; set; } = "";
    [JsonProperty("issueId")] public string IssueId { get; set; } = "";
    [JsonProperty("status")] public string Status { get; set; } = "";
    [JsonProperty("fixVersion", NullValueHandling = NullValueHandling.Ignore)]
    public string? FixVersion { get; set; }
    [JsonProperty("publicTitle", NullValueHandling = NullValueHandling.Ignore)]
    public string? PublicTitle { get; set; }
}
