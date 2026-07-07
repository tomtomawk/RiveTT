using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Telemetry;
using Xunit;

namespace RevitCortex.Tests.Telemetry;

public class TelemetryEventTests
{
    // JObject.Parse defaults to DateParseHandling.DateTime, which coerces any
    // ISO-8601-looking string value (like our "ts" field) into a Date-typed
    // JValue at parse time — unrelated to how TelemetryEvent itself serializes
    // Timestamp (a plain string). Parse with DateParseHandling.None so the
    // wire value round-trips as the raw string the API actually produces.
    private static JObject ParsePreservingStrings(string json)
    {
        using var stringReader = new StringReader(json);
        using var jsonReader = new JsonTextReader(stringReader) { DateParseHandling = DateParseHandling.None };
        return (JObject)JToken.ReadFrom(jsonReader);
    }

    [Fact]
    public void Serializes_WithCamelCaseWireNames_AndOmitsNullSanitizedMessage()
    {
        var evt = new TelemetryEvent
        {
            EventId = "e1", InstallationId = "i1", Kind = "error",
            Fingerprint = "a3f9c2e1b0d47f68", Tool = "create_dimensions",
            ErrorCode = "InvalidInput", FailureStage = "tool",
            MessageClass = "parameter_missing", MessageOrigin = "exception",
            SanitizedMessage = null, PluginVersion = "1.0.40",
            RevitVersion = "2025", Target = "R25", OsMajor = "Windows 10.0",
            Locale = "it", DurationMs = 12, ResponseBytes = 34,
            Timestamp = "2026-07-07T10:30:00Z"
        };

        var json = ParsePreservingStrings(JsonConvert.SerializeObject(evt));

        Assert.Equal(1, (int)json["schemaVersion"]!);
        Assert.Equal("e1", (string)json["eventId"]!);
        Assert.Equal("a3f9c2e1b0d47f68", (string)json["fingerprint"]!);
        Assert.Equal("2026-07-07T10:30:00Z", (string)json["ts"]!);
        Assert.Null(json["sanitizedMessage"]);
        Assert.Equal("exception", (string)json["messageOrigin"]!);
    }

    [Fact]
    public void KnownIssueMatch_RoundTrips()
    {
        var json = "{\"fingerprint\":\"abc\",\"issueId\":\"RC-014\",\"status\":\"fixed\",\"fixVersion\":\"1.0.42\",\"publicTitle\":\"t\"}";
        var m = JsonConvert.DeserializeObject<KnownIssueMatch>(json)!;
        Assert.Equal("RC-014", m.IssueId);
        Assert.Equal("1.0.42", m.FixVersion);
    }
}
