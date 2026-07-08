using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using RevitCortex.Core.Licensing;

namespace RevitCortex.Plugin.Licensing;

/// <summary>
/// Pure hashing/omission logic, testable without hardware. Each non-empty raw attribute
/// value is SHA-256-hashed (lowercase hex) and returned in input order; null/empty/
/// whitespace values are dropped. Never throws. PUBLIC so the unit test can reach it.
/// </summary>
public static class FingerprintHasher
{
    public static IReadOnlyList<string> Hash(IEnumerable<string?> rawValues)
    {
        var result = new List<string>();
        if (rawValues == null) return result;

        using var sha = SHA256.Create();
        foreach (var value in rawValues)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            result.Add(sb.ToString());
        }
        return result;
    }
}

/// <summary>
/// Windows hardware fingerprint (Fase 1: MachineGuid only). Reads MachineGuid from the
/// registry (read-only, benign — read by countless programs) and SHA-256-hashes it. The
/// HKLM READ is intentional and allowed (MachineGuid lives only there); the "never HKLM"
/// rule applies to WRITES (see AntiRollbackClock). No WMI, no MAC address (personal data),
/// no System.Management dependency. Missing/unreadable -> empty list, never throws. The
/// server applies the match threshold, so a single attribute is acceptable.
/// (Future extension, OUTSIDE these tasks: add BIOS/board serial via WMI behind a per-TFM
/// System.Management PackageReference + R27 gate + try/catch-omit.)
/// </summary>
public sealed class WindowsFingerprintProvider : IFingerprintProvider
{
    public IReadOnlyList<string> GetHashedAttributes()
    {
        return FingerprintHasher.Hash(new[] { TryReadMachineGuid() });
    }

    private static string? TryReadMachineGuid()
    {
        try
        {
            using var key = RegistryKey
                .OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography", writable: false);
            return key?.GetValue("MachineGuid") as string;
        }
        catch
        {
            return null;
        }
    }
}
