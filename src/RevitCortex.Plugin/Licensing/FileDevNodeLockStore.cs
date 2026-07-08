using System;
using System.IO;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Licensing;

namespace RevitCortex.Plugin.Licensing;

/// <summary>
/// File-backed <see cref="IDevNodeLockStore"/>: JSON `{ format, locks: { key: fingerprint } }`.
/// Dev/demo only. Missing/corrupt file → empty. A failed write returns false so the backend
/// fails activation rather than accepting an unpersisted lock. Atomic temp-replace write.
/// </summary>
public class FileDevNodeLockStore : IDevNodeLockStore
{
    private readonly string _path;

    public FileDevNodeLockStore(string path) { _path = path; }

    public string? GetBoundFingerprint(string licenseKey)
    {
        var locks = LoadLocks();
        return (string?)locks[licenseKey];
    }

    public bool TryBind(string licenseKey, string fingerprint)
    {
        try
        {
            var locks = LoadLocks();
            locks[licenseKey] = fingerprint;
            var root = new JObject { ["format"] = 1, ["locks"] = locks };

            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, root.ToString(Newtonsoft.Json.Formatting.Indented));
            if (File.Exists(_path)) File.Delete(_path);
            File.Move(tmp, _path);
            return true;
        }
        catch { return false; }
    }

    private JObject LoadLocks()
    {
        try
        {
            if (!File.Exists(_path)) return new JObject();
            var root = JObject.Parse(File.ReadAllText(_path));
            return root["locks"] as JObject ?? new JObject();
        }
        catch { return new JObject(); }
    }
}
