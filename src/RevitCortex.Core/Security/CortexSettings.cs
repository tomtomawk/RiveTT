using System.IO;
using Newtonsoft.Json;
using RevitCortex.Core.Hosting;

namespace RevitCortex.Core.Security;

/// <summary>
/// User-editable settings persisted at CortexEnvironment.Current.SettingsFilePath
/// (~/.revitcortex/settings.json in prod, ~/.revitcortex-dev/settings.json in dev).
/// Missing file or parse errors return defaults (all opt-in features disabled).
/// </summary>
public class CortexSettings
{
    /// <summary>
    /// When false (default), send_code_to_revit is refused at the tool-invocation boundary.
    /// The user must explicitly enable dynamic code execution via settings.json or the
    /// Revit plugin Settings UI. This is a hard gate, not a soft warning.
    /// </summary>
    [JsonProperty("EnableCodeExecution")]
    public bool EnableCodeExecution { get; set; } = false;

    /// <summary>TCP port for plugin-to-server communication.</summary>
    [JsonProperty("Port")]
    public int Port { get; set; } = CortexEnvironment.Current.DefaultPort;

    public static string DefaultPath => CortexEnvironment.Current.SettingsFilePath;

    public static CortexSettings Load(string? path = null)
    {
        var file = path ?? DefaultPath;
        try
        {
            if (!File.Exists(file)) return new CortexSettings();
            var json = File.ReadAllText(file);
            return JsonConvert.DeserializeObject<CortexSettings>(json) ?? new CortexSettings();
        }
        catch
        {
            return new CortexSettings();
        }
    }

    public void Save(string? path = null)
    {
        var file = path ?? DefaultPath;
        var dir = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(file, JsonConvert.SerializeObject(this, Formatting.Indented));
    }
}
