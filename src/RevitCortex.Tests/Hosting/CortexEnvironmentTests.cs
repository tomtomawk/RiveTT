using RevitCortex.Core.Hosting;
using Xunit;

namespace RevitCortex.Tests.Hosting;

public class CortexEnvironmentTests
{
    [Fact]
    public void CreateDefault_UsesLocalApplicationData()
    {
        var expected = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "MCPRVTT27");

        Assert.Equal(expected, CortexEnvironment.CreateDefault().RootFolder);
    }

    [Fact]
    public void Paths_DeriveFromRootFolder()
    {
        var env = CortexEnvironment.ForTests(@"C:\temp\MCPRVTT27");

        Assert.Equal(@"C:\temp\MCPRVTT27\audit.jsonl", env.AuditLogPath);
        Assert.Equal(@"C:\temp\MCPRVTT27\scripts", env.ScriptsFolder);
    }
}
