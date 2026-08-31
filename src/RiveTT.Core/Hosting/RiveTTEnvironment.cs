using System;
using System.IO;

namespace RiveTT.Core.Hosting;

/// <summary>
/// Local paths used by the automatic Revit integration (2026.5+ or 2027).
/// </summary>
public class RiveTTEnvironment
{
    public string RootFolder { get; }

    public string AuditLogPath => Path.Combine(RootFolder, "audit.jsonl");
    public string ScriptsFolder => Path.Combine(RootFolder, "scripts");

    /// <summary>
    /// Where a startup failure is written for a human to find. Inside Revit,
    /// <c>Trace.WriteLine</c> goes to OutputDebugString and is invisible without a
    /// debugger attached — which is how the audit log once stopped growing unnoticed,
    /// and how a ribbon that fails to build would leave a session locked read-only with
    /// no explanation anywhere.
    /// </summary>
    public string StartupLogPath => Path.Combine(RootFolder, "startup.errors.log");

    private RiveTTEnvironment(string rootFolder)
    {
        RootFolder = rootFolder;
    }

    private static RiveTTEnvironment? _current;

    /// <summary>Process-wide storage location.</summary>
    public static RiveTTEnvironment Current
    {
        get
        {
            var c = _current;
            if (c == null)
            {
                c = CreateDefault();
                _current = c;
            }
            return c;
        }
    }

    /// <summary>Test seam: force a storage location (pass null to re-detect).</summary>
    public static void OverrideForTests(RiveTTEnvironment? env) { _current = env; }

    public static RiveTTEnvironment CreateDefault() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RiveTT"));

    /// <summary>Test-only storage location that never touches user data.</summary>
    public static RiveTTEnvironment ForTests(string rootFolder) =>
        new(rootFolder);

    /// <summary>
    /// Records a startup failure where a human can read it, with the CONSEQUENCE spelled
    /// out rather than only the exception: "ribbon panel not created" does not tell an
    /// architect that their session can no longer be unlocked.
    ///
    /// Best-effort by construction. This is called from a catch block on the Revit UI
    /// thread during OnStartup; throwing here would take down the add-in over a log line.
    /// </summary>
    public void ReportStartupFailure(string component, Exception exception, string consequence)
    {
        try
        {
            Directory.CreateDirectory(RootFolder);
            File.AppendAllText(StartupLogPath,
                $"{DateTime.UtcNow:o} component={component}{Environment.NewLine}" +
                $"  consequence: {consequence}{Environment.NewLine}" +
                $"  exception: {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Nothing left to fall back to; the connector must still start.
        }
    }
}
