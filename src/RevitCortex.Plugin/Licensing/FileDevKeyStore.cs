using System;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Licensing;

namespace RevitCortex.Plugin.Licensing;

/// <summary>
/// File-backed <see cref="IDevKeyStore"/>: persists the full RSA keypair as base64
/// RSAParameters fields in a JSON file (cross-target — never ToXmlString). Dev/demo only;
/// gated to Debug builds by LicenseBootstrap. A corrupt file is renamed ".bad" and the
/// key is regenerated (old debug tokens stop verifying — acceptable local demo state).
/// </summary>
public class FileDevKeyStore : IDevKeyStore
{
    private readonly string _path;
    private RSAParameters? _cached;

    public FileDevKeyStore(string path) { _path = path; }

    public RSAParameters LoadOrCreate()
    {
        if (_cached != null) return _cached.Value;

        var loaded = TryLoad();
        if (loaded != null) { _cached = loaded.Value; return _cached.Value; }

        using (var rsa = RSA.Create(2048))
        {
            var p = rsa.ExportParameters(true);
            Save(p);
            _cached = p;
            return p;
        }
    }

    public RSAParameters PublicOnly()
    {
        var full = LoadOrCreate();
        return new RSAParameters { Modulus = full.Modulus, Exponent = full.Exponent };
    }

    private RSAParameters? TryLoad()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var o = JObject.Parse(File.ReadAllText(_path));
            return new RSAParameters
            {
                Modulus  = B64(o, "modulus"),
                Exponent = B64(o, "exponent"),
                D        = B64(o, "d"),
                P        = B64(o, "p"),
                Q        = B64(o, "q"),
                DP       = B64(o, "dp"),
                DQ       = B64(o, "dq"),
                InverseQ = B64(o, "inverseQ"),
            };
        }
        catch
        {
            // Corrupt: preserve as .bad (best-effort), then signal regenerate.
            try { if (File.Exists(_path + ".bad")) File.Delete(_path + ".bad"); File.Move(_path, _path + ".bad"); }
            catch { }
            return null;
        }
    }

    private void Save(RSAParameters p)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        var o = new JObject
        {
            ["format"] = 1,
            ["algorithm"] = "RSA-2048-PKCS1-SHA256",
            ["modulus"]  = Conv(p.Modulus),
            ["exponent"] = Conv(p.Exponent),
            ["d"]        = Conv(p.D),
            ["p"]        = Conv(p.P),
            ["q"]        = Conv(p.Q),
            ["dp"]       = Conv(p.DP),
            ["dq"]       = Conv(p.DQ),
            ["inverseQ"] = Conv(p.InverseQ),
        };
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, o.ToString(Newtonsoft.Json.Formatting.Indented));
        if (File.Exists(_path)) File.Delete(_path);
        File.Move(tmp, _path);
    }

    private static string? Conv(byte[]? b) => b == null ? null : Convert.ToBase64String(b);
    private static byte[]? B64(JObject o, string k)
    {
        var v = (string?)o[k];
        return string.IsNullOrEmpty(v) ? null : Convert.FromBase64String(v);
    }
}
