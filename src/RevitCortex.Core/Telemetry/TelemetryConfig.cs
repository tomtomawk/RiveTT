using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Core.Telemetry;

/// <summary>
/// Telemetry settings backed by the active profile's settings.json
/// (CortexEnvironment.Current.SettingsFilePath). All writes are merge-writes
/// (read JObject, set keys, write back) so unrelated keys are never dropped.
/// EffectiveEnabled is THE consent gate: enabled AND answered AND consent
/// version current.
/// </summary>
public class TelemetryConfig
{
    public const string CurrentConsentVersion = "2026-07-07";

    private readonly string _path;
    private JObject _root;

    private TelemetryConfig(string path, JObject root)
    {
        _path = path;
        _root = root;
    }

    public static TelemetryConfig Load(string? path = null)
    {
        var p = path ?? Hosting.CortexEnvironment.Current.SettingsFilePath;
        JObject root;
        try
        {
            root = File.Exists(p) ? JObject.Parse(File.ReadAllText(p)) : new JObject();
        }
        catch
        {
            root = new JObject(); // unreadable settings must not crash telemetry
        }
        return new TelemetryConfig(p, root);
    }

    public bool EnableTelemetry => ReadBool("EnableTelemetry", false);
    public bool ConsentAnswered => ReadBool("TelemetryConsentAnswered", false);
    public string StoredConsentVersion => ReadString("TelemetryConsentVersion", "");
    public string Endpoint => ReadString("TelemetryEndpoint",
        Hosting.CortexEnvironment.Current.DefaultTelemetryEndpoint);
    public long BottleneckDurationMs => ReadLong("BottleneckDurationMs", 10000);
    public long BottleneckResponseBytes => ReadLong("BottleneckResponseBytes", 512000);
    public int ZipPromptFailureThreshold => (int)ReadLong("ZipPromptFailureThreshold", 3);

    public bool NeedsConsentPrompt =>
        !ConsentAnswered || StoredConsentVersion != CurrentConsentVersion;

    public bool EffectiveEnabled =>
        EnableTelemetry && ConsentAnswered && StoredConsentVersion == CurrentConsentVersion;

    public void MarkConsent(bool enabled)
    {
        MergeWrite(root =>
        {
            root["EnableTelemetry"] = enabled;
            root["TelemetryConsentAnswered"] = true;
            root["TelemetryConsentVersion"] = CurrentConsentVersion;
        });
    }

    public string EnsureInstallationId()
    {
        var existing = ReadString("InstallationId", "");
        if (!string.IsNullOrEmpty(existing)) return existing;
        var id = Guid.NewGuid().ToString();
        MergeWrite(root => root["InstallationId"] = id);
        return id;
    }

    private void MergeWrite(Action<JObject> mutate)
    {
        try
        {
            JObject root;
            try
            {
                root = File.Exists(_path) ? JObject.Parse(File.ReadAllText(_path)) : new JObject();
            }
            catch { root = new JObject(); }

            mutate(root);

            var dir = Path.GetDirectoryName(_path);
            if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_path, root.ToString());
            _root = root;
        }
        catch { /* settings write failure must never crash the host */ }
    }

    private bool ReadBool(string key, bool fallback)
    {
        var t = _root[key];
        return t != null && t.Type == JTokenType.Boolean ? (bool)t : fallback;
    }

    private string ReadString(string key, string fallback)
    {
        var t = _root[key];
        return t != null && t.Type == JTokenType.String ? ((string?)t ?? fallback) : fallback;
    }

    private long ReadLong(string key, long fallback)
    {
        var t = _root[key];
        return t != null && (t.Type == JTokenType.Integer) ? (long)t : fallback;
    }
}
