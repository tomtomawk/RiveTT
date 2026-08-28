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

}
