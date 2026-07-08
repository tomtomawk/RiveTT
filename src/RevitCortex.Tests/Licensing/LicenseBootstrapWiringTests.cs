using System;
using System.IO;
using System.Reflection;
using RevitCortex.Core.Hosting;
using Xunit;

namespace RevitCortex.Tests.Licensing;

// Regression guard: LicenseBootstrap.Init's dev-backend branch is compiled under
// DEBUG_R23..DEBUG_R27 (NOT bare DEBUG — this project's configs are "Debug RXX"). If the
// guard ever regresses to `#if DEBUG`, the dev branch goes dead and Gate/Manager become
// null even in Debug — this test fails first. The test assembly compiles with DEBUG_R25.
public class LicenseBootstrapWiringTests : IDisposable
{
    private readonly string _dir;
    public LicenseBootstrapWiringTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "rc-bootstrap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static object? GetStatic(string propName)
    {
        var t = Type.GetType("RevitCortex.Plugin.Licensing.LicenseBootstrap, RevitCortex.Plugin")
                ?? throw new InvalidOperationException("LicenseBootstrap type not found");
        var p = t.GetProperty(propName, BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException(propName + " not found");
        return p.GetValue(null);
    }

    private static void Init(CortexEnvironment env)
    {
        var t = Type.GetType("RevitCortex.Plugin.Licensing.LicenseBootstrap, RevitCortex.Plugin")
                ?? throw new InvalidOperationException("LicenseBootstrap type not found");
        var m = t.GetMethod("Init", BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("Init not found");
        m.Invoke(null, new object[] { env });
    }

    [Fact]
    public void Init_InDebugBuild_BuildsRealGateAndManager()
    {
        // If the dev-backend #if guard is dead (regressed to `#if DEBUG`), Init takes the
        // #else fail-closed branch and both are null → this fails.
        Init(CortexEnvironment.ForTests(_dir));
        Assert.NotNull(GetStatic("Gate"));
        Assert.NotNull(GetStatic("Manager"));
    }
}
