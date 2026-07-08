namespace RevitCortex.Core.Telemetry;

/// <summary>Host facts stamped on every event. Filled by the Plugin (or tests).</summary>
public class TelemetryEnvironment
{
    public string PluginVersion { get; set; } = "";
    public string RevitVersion { get; set; } = "";
    public string Target { get; set; } = "";
    public string OsMajor { get; set; } = "";
    public string Locale { get; set; } = "";
}
