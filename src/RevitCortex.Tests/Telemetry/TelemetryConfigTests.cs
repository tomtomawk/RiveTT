using System;
using System.IO;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Telemetry;
using Xunit;

namespace RevitCortex.Tests.Telemetry;

public class TelemetryConfigTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(),
        "rc-tests-" + Guid.NewGuid().ToString("N"), "settings.json");

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_path)!, true); } catch { }
    }

    [Fact]
    public void Defaults_TelemetryDisabled_ConsentNotAnswered()
    {
        var c = TelemetryConfig.Load(_path);
        Assert.False(c.EnableTelemetry);
        Assert.False(c.ConsentAnswered);
        Assert.True(c.NeedsConsentPrompt);
        Assert.False(c.EffectiveEnabled);
        Assert.Equal(10000, c.BottleneckDurationMs);
        Assert.Equal(512000, c.BottleneckResponseBytes);
        Assert.Equal(3, c.ZipPromptFailureThreshold);
        Assert.Equal("https://ingest.revitcortex.dev", c.Endpoint);
    }

    [Fact]
    public void MarkConsent_True_EnablesAndStampsVersion_PreservingOtherKeys()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{\"Port\":8080,\"EnableDynamo\":true}");

        var c = TelemetryConfig.Load(_path);
        c.MarkConsent(true);

        var reloaded = TelemetryConfig.Load(_path);
        Assert.True(reloaded.EnableTelemetry);
        Assert.True(reloaded.ConsentAnswered);
        Assert.False(reloaded.NeedsConsentPrompt);
        Assert.True(reloaded.EffectiveEnabled);

        var raw = JObject.Parse(File.ReadAllText(_path));
        Assert.Equal(8080, (int)raw["Port"]!);          // merge-write proof
        Assert.True((bool)raw["EnableDynamo"]!);
        Assert.Equal(TelemetryConfig.CurrentConsentVersion, (string)raw["TelemetryConsentVersion"]!);
    }

    [Fact]
    public void ConsentVersionBump_RequiresReprompt_AndDisablesEffective()
    {
        var c = TelemetryConfig.Load(_path);
        c.MarkConsent(true);

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var raw = JObject.Parse(File.ReadAllText(_path));
        raw["TelemetryConsentVersion"] = "2000-01-01";  // simulate older consent
        File.WriteAllText(_path, raw.ToString());

        var stale = TelemetryConfig.Load(_path);
        Assert.True(stale.NeedsConsentPrompt);
        Assert.False(stale.EffectiveEnabled);
    }

    [Fact]
    public void EnsureInstallationId_GeneratesOnce_AndPersists()
    {
        var c = TelemetryConfig.Load(_path);
        var id1 = c.EnsureInstallationId();
        var id2 = TelemetryConfig.Load(_path).EnsureInstallationId();
        Assert.Equal(id1, id2);
        Assert.True(Guid.TryParse(id1, out _));
    }
}
