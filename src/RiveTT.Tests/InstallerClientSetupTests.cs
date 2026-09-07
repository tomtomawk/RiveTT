using System.Diagnostics;
using System.IO.Compression;
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
    public void ClientTasks_BundleConfigAndSkill_WithoutDeletingPersonalSkillDirectory()
    {
        var installer = File.ReadAllText(RepositoryFile.Path("builder", "installer", "RiveTT.iss"));
        Assert.Contains("Configurer pour Claude (config + skill)", installer);
        Assert.Contains("Configurer pour ChatGPT (config + skill)", installer);
        Assert.DoesNotContain("Name: \"codexskill\"", installer);
        Assert.Contains("Flags: ignoreversion recursesubdirs; Tasks: mcpcodex", installer);
        Assert.Contains("PrepareClaudeSkill()", installer);
        Assert.Contains(".agents\\skills\\rivett", installer);
        Assert.DoesNotContain("Type: filesandordirs; Name: \"{code:CodexSkillDir}\"", installer);
    }

    [Fact]
    public void ClaudeArchive_ContainsOnlyUnifiedSkill_AndIgnoresLegacyReferences()
    {
        using var fixture = new SkillFixture();
        Directory.CreateDirectory(Path.Combine(fixture.Docs, "references"));
        File.WriteAllText(Path.Combine(fixture.Docs, "README.md"), "Guide");
        File.WriteAllText(Path.Combine(fixture.Docs, "references", "old.md"), "Old");
        Assert.Equal(0, fixture.Run());
        using (var zip = ZipFile.OpenRead(fixture.Archive))
        {
            Assert.NotNull(zip.GetEntry("rivett/SKILL.md"));
            Assert.Single(zip.Entries);
            Assert.Null(zip.GetEntry("rivett/README.md"));
            Assert.Null(zip.GetEntry("rivett/references/old.md"));
            Assert.Null(zip.GetEntry("rivett/agents/openai.yaml"));
        }
        File.Delete(Path.Combine(fixture.Docs, "references", "old.md"));
        File.WriteAllText(Path.Combine(fixture.Docs, "references", "new.md"), "New");
        Assert.Equal(0, fixture.Run());
        using var updated = ZipFile.OpenRead(fixture.Archive);
        Assert.Null(updated.GetEntry("rivett/references/old.md"));
        Assert.Null(updated.GetEntry("rivett/references/new.md"));
        Assert.Single(updated.Entries);
        Assert.Equal("untouched", File.ReadAllText(fixture.ClaudeConfig));
    }

    [Fact]
    public void ClaudeArchive_MissingSkill_FailsAndPreservesPreviousArchiveAndConfig()
    {
        using var fixture = new SkillFixture();
        Assert.Equal(0, fixture.Run());
        var original = File.ReadAllBytes(fixture.Archive);
        File.Delete(Path.Combine(fixture.Docs, "SKILL.md"));
        Assert.Equal(1, fixture.Run());
        Assert.Equal(original, File.ReadAllBytes(fixture.Archive));
        Assert.Equal("untouched", File.ReadAllText(fixture.ClaudeConfig));
    }

    private sealed class SkillFixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "RiveTT-client-test-" + Guid.NewGuid().ToString("N"));
        internal string Docs => Path.Combine(root, "documentation");
        internal string Archive => Path.Combine(root, "integrations", "Claude", "rivett.zip");
        internal string ClaudeConfig => Path.Combine(root, "roaming", "Claude", "claude_desktop_config.json");

        internal SkillFixture()
        {
            Directory.CreateDirectory(Docs);
            Directory.CreateDirectory(Path.GetDirectoryName(ClaudeConfig)!);
            File.WriteAllText(ClaudeConfig, "untouched");
            File.WriteAllText(Path.Combine(Docs, "SKILL.md"), "---\nname: rivett\ndescription: Test\n---\n# Test");
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
                         "-Client", "Claude", "-PrepareSkill", "-DocumentationPath", Docs,
                         "-SkillArchivePath", Archive })
                start.ArgumentList.Add(arg);
            start.Environment["LOCALAPPDATA"] = Path.Combine(root, "local");
            start.Environment["APPDATA"] = Path.Combine(root, "roaming");
            start.Environment["USERPROFILE"] = root;
            start.Environment["CODEX_HOME"] = Path.Combine(root, "codex");
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30000))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("Skill preparation exceeded 30 seconds.");
            }
            Task.WaitAll(output, error);
            return process.ExitCode;
        }

        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
