using System;
using System.IO;
using Microsoft.Win32;
using RevitCortex.Core.Licensing;

namespace RevitCortex.Plugin.Licensing;

/// <summary>
/// Thin persistence seam for a redundant high-water mark. Real adapters write to HKCU and
/// to a ProgramData file ONLY (spec §9 / fix #5: never HKLM for writes, never license.json
/// which is user-writable). A fake drives the monotonic logic in unit tests.
/// </summary>
public interface IHighWaterMarkStore
{
    DateTime? Read();        // null if unset/unreadable
    void Write(DateTime utc);
}

/// <summary>HKCU-backed high-water mark. All access is try/catch: a blocked/absent
/// registry yields null on read and swallows the write. Stores UTC ticks as a string
/// under HKCU\Software\RevitCortex\LicenseHighWaterMarkTicks.</summary>
public sealed class RegistryHighWaterMarkStore : IHighWaterMarkStore
{
    private const string SubKey = @"Software\RevitCortex";
    private const string ValueName = "LicenseHighWaterMarkTicks";

    public DateTime? Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SubKey, writable: false);
            var raw = key?.GetValue(ValueName) as string;
            if (string.IsNullOrEmpty(raw)) return null;
            if (!long.TryParse(raw, out var ticks)) return null;
            return new DateTime(ticks, DateTimeKind.Utc);
        }
        catch { return null; }
    }

    public void Write(DateTime utc)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(SubKey);
            key?.SetValue(ValueName, utc.ToUniversalTime().Ticks.ToString(), RegistryValueKind.String);
        }
        catch { /* registry write blocked -> anti-rollback degrades, never crashes */ }
    }
}

/// <summary>ProgramData-file high-water mark (second redundant source, fix #5). Stores UTC
/// ticks as text under %ProgramData%\RevitCortex\license-hwm.txt. All access is try/catch.</summary>
public sealed class ProgramDataHighWaterMarkStore : IHighWaterMarkStore
{
    private readonly string _path;

    public ProgramDataHighWaterMarkStore()
    {
        _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "RevitCortex", "license-hwm.txt");
    }

    public DateTime? Read()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var raw = File.ReadAllText(_path).Trim();
            if (!long.TryParse(raw, out var ticks)) return null;
            return new DateTime(ticks, DateTimeKind.Utc);
        }
        catch { return null; }
    }

    public void Write(DateTime utc)
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(_path, utc.ToUniversalTime().Ticks.ToString());
        }
        catch { /* ProgramData not writable -> degrade, never crash */ }
    }
}

/// <summary>
/// System clock with a monotonic high-water mark. On construction it computes the maximum
/// of {UtcNow, HKCU mark, ProgramData mark} and persists that maximum back to any store
/// that is behind it, so the mark only ever advances. UtcNow reports the real (possibly
/// rolled-back) time; LicenseManager compares UtcNow against HighWaterMarkUtc to detect
/// rollback (spec §4 point 8). Every source read/write is total (failure -> ignored).
/// </summary>
public sealed class AntiRollbackClock : ISystemClock
{
    private readonly Func<DateTime> _now;

    public AntiRollbackClock(Func<DateTime> now, IHighWaterMarkStore hkcu, IHighWaterMarkStore programData)
    {
        _now = now ?? (() => DateTime.UtcNow);

        var current = _now().ToUniversalTime();
        var max = current;

        var a = SafeRead(hkcu);
        if (a.HasValue && a.Value > max) max = a.Value;
        var b = SafeRead(programData);
        if (b.HasValue && b.Value > max) max = b.Value;

        HighWaterMarkUtc = max;

        if (!a.HasValue || max > a.Value) SafeWrite(hkcu, max);
        if (!b.HasValue || max > b.Value) SafeWrite(programData, max);
    }

    public DateTime UtcNow => _now().ToUniversalTime();

    public DateTime HighWaterMarkUtc { get; }

    private static DateTime? SafeRead(IHighWaterMarkStore store)
    {
        try { return store?.Read(); } catch { return null; }
    }

    private static void SafeWrite(IHighWaterMarkStore store, DateTime utc)
    {
        try { store?.Write(utc); } catch { }
    }
}
