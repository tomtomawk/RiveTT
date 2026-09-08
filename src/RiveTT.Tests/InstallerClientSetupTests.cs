using System.Diagnostics;
using Xunit;

namespace RiveTT.Tests;

public class InstallerClientSetupTests
{
    [Fact]
    public void ProductDocumentationContainsOnlyStandaloneSkill()
    {
        var directory = RepositoryFile.Path("src", "resources", "documentation");
        Assert.Equal(new[] { "SKILL.md" }, Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(directory, path)).ToArray());
        var skill = File.ReadAllText(Path.Combine(directory, "SKILL.md"));
        Assert.Contains("name: rivett", skill);
        Assert.Contains("<!-- BEGIN GENERATED TOOL INVENTORY -->", skill);
        Assert.Contains("<!-- END GENERATED TOOL INVENTORY -->", skill);
    }

    [Fact]
    public void Installer_RenamesTheGuideButKeepsChatGptSkillName()
    {
        var installer = File.ReadAllText(RepositoryFile.Path("builder", "installer", "RiveTT.iss"));
        Assert.Contains("DestDir: \"{app}\\documentation\"; \\", installer);
        Assert.Contains("DestName: \"skills_RiveTT.md\"; Flags: ignoreversion", installer);
        Assert.Contains("DestDir: \"{code:CodexSkillDir}\"; \\", installer);
        Assert.Contains("DestName: \"SKILL.md\"; Flags: ignoreversion; Tasks: mcpcodex", installer);
        Assert.Contains("{app}\\documentation\\skills_RiveTT.md", installer);
        Assert.DoesNotContain("docs\\claude", installer);
    }

    [Fact]
    public void ClientTasks_ConfigureClaudeConnectionWithoutPackagingAClaudeSkill()
    {
        var installer = File.ReadAllText(RepositoryFile.Path("builder", "installer", "RiveTT.iss"));
        Assert.Contains("Configurer la connexion MCP pour Claude", installer);
        Assert.Contains("Configurer pour ChatGPT (config + skill)", installer);
        Assert.DoesNotContain("Name: \"codexskill\"", installer);
        Assert.Contains("DestName: \"SKILL.md\"; Flags: ignoreversion; Tasks: mcpcodex", installer);
        Assert.DoesNotContain("PrepareClaudeSkill()", installer);
        Assert.DoesNotContain("integrations\\Claude\\rivett.zip", installer);
        Assert.Contains(".agents\\skills\\rivett", installer);
        Assert.DoesNotContain("LegacySkill", installer);
        Assert.DoesNotContain("ClientHome", installer);
        Assert.DoesNotContain("Type: filesandordirs; Name: \"{code:CodexSkillDir}\"", installer);
    }

    [Fact]
    public void ClaudeRegistrationReport_IdentifiesTheScriptAndTheDetectionFailure()
    {
        var installer = File.ReadAllText(RepositoryFile.Path("builder", "installer", "RiveTT.iss"));
        Assert.Contains("Claude Desktop non détecté", installer);
        Assert.Contains("Lancez Claude une fois", installer);
        Assert.Contains("Script : ", installer);
        Assert.Contains("register-mcp-Claude.log", installer);
        Assert.Contains("si RiveTT n''existe pas encore dans", installer);
        Assert.Contains("{app}\\documentation\\skills_RiveTT.md", installer);
    }

    [Fact]
    public void ClaudeStoreOnlyConfiguration_IsDetectedAndRegistered()
    {
        using var fixture = new StoreClaudeFixture();
        Assert.Equal(0, fixture.Run());

        var config = File.ReadAllText(fixture.Config);
        Assert.Contains("\"RiveTT\"", config);
        Assert.Contains(fixture.ServerPath.Replace("\\", "\\\\"), config);
        Assert.False(Directory.Exists(Path.Combine(fixture.AppData, "Claude")));
    }

    private sealed class StoreClaudeFixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "RiveTT-store-claude-" + Guid.NewGuid().ToString("N"));
        internal string AppData => Path.Combine(root, "roaming");
        private string LocalAppData => Path.Combine(root, "local");
        internal string Config => Path.Combine(LocalAppData, "Packages", "Claude_anotherpublisherid",
            "LocalCache", "Roaming", "Claude", "claude_desktop_config.json");
        internal string ServerPath => Path.Combine(root, "RiveTT.Server.exe");

        internal StoreClaudeFixture()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Config)!);
            File.WriteAllText(Config, "{\n  \"mcpServers\": {}\n}\n");
            File.WriteAllText(ServerPath, "test server");
        }

        internal int Run()
        {
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "WindowsPowerShell", "v1.0", "powershell.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                         RepositoryFile.Path("src", "resources", "register-mcp.ps1"),
                         "-Client", "Claude", "-ServerPath", ServerPath })
                start.ArgumentList.Add(arg);
            start.Environment["LOCALAPPDATA"] = LocalAppData;
            start.Environment["APPDATA"] = AppData;
            start.Environment["USERPROFILE"] = root;
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30000))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("Store Claude registration exceeded 30 seconds.");
            }
            Task.WaitAll(output, error);
            return process.ExitCode;
        }

        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
