using System;
using System.IO;

namespace RevitCortex.Core.Hosting;

/// <summary>
/// Central prod/dev profile: every environment-dependent value (folders,
/// default port, telemetry endpoint) comes from here. The dev profile is
/// detected from the addin folder name (deploy-dev.ps1 installs into
/// "RevitCortexDev\"), so prod and dev plugins coexist on the same machine
/// without sharing settings, audit, queue, reports, or port.
/// </summary>
public class CortexEnvironment
{
    public string ProfileName { get; }
    public bool IsDev { get; }
    public string RootFolder { get; }
    public int DefaultPort { get; }
    public string DefaultTelemetryEndpoint { get; }

    public string SettingsFilePath => Path.Combine(RootFolder, "settings.json");
    public string AuditLogPath => Path.Combine(RootFolder, "audit.jsonl");
    public string TelemetryQueuePath => Path.Combine(RootFolder, "telemetry-queue.jsonl");
    public string SupportReportsFolder => Path.Combine(RootFolder, "support-reports");
    public string ScriptsFolder => Path.Combine(RootFolder, "scripts");

    private CortexEnvironment(string profileName, bool isDev, string rootFolder,
        int defaultPort, string defaultTelemetryEndpoint)
    {
        ProfileName = profileName;
        IsDev = isDev;
        RootFolder = rootFolder;
        DefaultPort = defaultPort;
        DefaultTelemetryEndpoint = defaultTelemetryEndpoint;
    }

    private static CortexEnvironment? _current;

    /// <summary>Process-wide profile, detected from the executing assembly's folder.</summary>
    public static CortexEnvironment Current
    {
        get
        {
            var c = _current;
            if (c == null)
            {
                string? location = null;
                try { location = typeof(CortexEnvironment).Assembly.Location; } catch { }
                c = Detect(location);
                _current = c;
            }
            return c;
        }
    }

    /// <summary>Test seam: force a profile (pass null to re-detect).</summary>
    public static void OverrideForTests(CortexEnvironment? env) { _current = env; }

    public static CortexEnvironment Detect(string? assemblyLocation)
    {
        bool dev = false;
        try
        {
            var dir = Path.GetDirectoryName(assemblyLocation ?? "") ?? "";
            dev = dir.IndexOf("RevitCortexDev", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch { }
        return dev ? Dev() : Prod();
    }

    public static CortexEnvironment Prod() => new CortexEnvironment(
        "prod", false, HomePath(".revitcortex"), 8080, "https://ingest.revitcortex.dev");

    public static CortexEnvironment Dev() => new CortexEnvironment(
        "dev", true, HomePath(".revitcortex-dev"), 8081, "http://127.0.0.1:8787");

    /// <summary>Test-only: a dev-profile environment rooted at an arbitrary folder, so
    /// tests can exercise the dev stack without touching the real ~/.revitcortex-dev.</summary>
    public static CortexEnvironment ForTests(string rootFolder) =>
        new CortexEnvironment("dev", true, rootFolder, 8081, "");

    private static string HomePath(string folder) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), folder);
}
