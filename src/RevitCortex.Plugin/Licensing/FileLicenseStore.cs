using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Hosting;
using RevitCortex.Core.Licensing;

namespace RevitCortex.Plugin.Licensing;

/// <summary>
/// Persists the license.json envelope (spec §5) in the active profile's RootFolder —
/// NEVER settings.json (D3: settings.json is merge-written by telemetry; sharing it is
/// the v1.0.36 corruption class). Load returns null on any failure; Save swallows all I/O
/// errors. Writes are atomic (temp + File.Replace, fallback delete+Move) so a crash
/// mid-write never leaves a truncated file. I/O discipline mirrors TelemetryConfig.
/// </summary>
public sealed class FileLicenseStore : ILicenseStore
{
    private readonly string _path;

    public FileLicenseStore(string? path = null)
    {
        _path = path ?? Path.Combine(CortexEnvironment.Current.RootFolder, "license.json");
    }

    public StoredLicenseState? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var root = JObject.Parse(File.ReadAllText(_path));

            var token = (string?)root["token"] ?? "";
            var last = ReadUtcNullable(root["lastOnlineCheckUtc"]);
            var hwm = ReadUtcNullable(root["highWaterMarkUtc"]) ?? DateTime.MinValue;

            return new StoredLicenseState(token, last, hwm);
        }
        catch
        {
            return null; // missing/corrupt/unreadable must never crash the host
        }
    }

    public void Save(StoredLicenseState state)
    {
        if (state == null) return;
        try
        {
            var root = new JObject
            {
                ["token"] = state.Token,
                ["highWaterMarkUtc"] = state.HighWaterMarkUtc.ToUniversalTime()
                    .ToString("yyyy-MM-ddTHH:mm:ssZ"),
            };
            if (state.LastOnlineCheckUtc.HasValue)
                root["lastOnlineCheckUtc"] = state.LastOnlineCheckUtc.Value.ToUniversalTime()
                    .ToString("yyyy-MM-ddTHH:mm:ssZ");

            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, root.ToString());

            if (File.Exists(_path))
            {
                try { File.Replace(tmp, _path, null); }
                catch { File.Delete(_path); File.Move(tmp, _path); }
            }
            else
            {
                File.Move(tmp, _path);
            }
        }
        catch
        {
            try { var tmp = _path + ".tmp"; if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    private static DateTime? ReadUtcNullable(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null) return null;
        try
        {
            if (token.Type == JTokenType.Date)
                return ((DateTime)token).ToUniversalTime();
            var s = (string?)token;
            if (string.IsNullOrEmpty(s)) return null;
            return DateTime.Parse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        }
        catch { return null; }
    }
}
