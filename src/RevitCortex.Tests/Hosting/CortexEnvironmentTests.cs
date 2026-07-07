using RevitCortex.Core.Hosting;
using Xunit;

namespace RevitCortex.Tests.Hosting;

public class CortexEnvironmentTests
{
    [Fact]
    public void Detect_AddinFolderContainsRevitCortexDev_IsDevProfile()
    {
        var env = CortexEnvironment.Detect(
            @"C:\Users\x\AppData\Roaming\Autodesk\Revit\Addins\2025\RevitCortexDev\RevitCortex.Plugin.dll");
        Assert.True(env.IsDev);
        Assert.Equal("dev", env.ProfileName);
        Assert.EndsWith(".revitcortex-dev", env.RootFolder);
        Assert.Equal(8081, env.DefaultPort);
        Assert.Equal("http://127.0.0.1:8787", env.DefaultTelemetryEndpoint);
    }

    [Fact]
    public void Detect_ProductionFolder_IsProdProfile()
    {
        var env = CortexEnvironment.Detect(
            @"C:\ProgramData\Autodesk\Revit\Addins\2025\RevitCortex\RevitCortex.Plugin.dll");
        Assert.False(env.IsDev);
        Assert.EndsWith(".revitcortex", env.RootFolder);
        Assert.Equal(8080, env.DefaultPort);
        Assert.Equal("https://ingest.revitcortex.dev", env.DefaultTelemetryEndpoint);
    }

    [Fact]
    public void Detect_NullOrGarbage_FallsBackToProd()
    {
        Assert.False(CortexEnvironment.Detect(null).IsDev);
        Assert.False(CortexEnvironment.Detect("???").IsDev);
    }

    [Fact]
    public void Paths_DeriveFromRootFolder()
    {
        var env = CortexEnvironment.Detect(@"C:\x\RevitCortexDev\p.dll");
        Assert.EndsWith(@".revitcortex-dev\settings.json", env.SettingsFilePath);
        Assert.EndsWith(@".revitcortex-dev\audit.jsonl", env.AuditLogPath);
        Assert.EndsWith(@".revitcortex-dev\telemetry-queue.jsonl", env.TelemetryQueuePath);
        Assert.EndsWith(@".revitcortex-dev\support-reports", env.SupportReportsFolder);
    }
}
