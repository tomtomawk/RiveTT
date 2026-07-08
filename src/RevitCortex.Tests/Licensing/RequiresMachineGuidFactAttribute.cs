using Microsoft.Win32;
using Xunit;

namespace RevitCortex.Tests.Licensing;

/// <summary>
/// Skips when HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid cannot be read
/// (non-Windows CI, restricted registry). Mirrors RequiresRevitApiFact: an
/// environmental absence becomes an honest Skip, not a failure.
/// </summary>
public sealed class RequiresMachineGuidFactAttribute : FactAttribute
{
    public RequiresMachineGuidFactAttribute()
    {
        if (!IsReadable())
            Skip = "Requires HKLM MachineGuid (real Windows machine registry).";
    }

    private static bool IsReadable()
    {
        try
        {
            using var key = RegistryKey
                .OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography", writable: false);
            return key?.GetValue("MachineGuid") is string s && !string.IsNullOrWhiteSpace(s);
        }
        catch
        {
            return false;
        }
    }
}
