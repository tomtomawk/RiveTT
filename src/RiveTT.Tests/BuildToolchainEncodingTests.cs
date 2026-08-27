using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace RiveTT.Tests;

/// <summary>
/// Locates a file of the build toolchain from the test output directory.
/// bin/Release/net10.0-windows -> RiveTT.Tests -> src -> repository root is five levels up,
/// the same convention the source-text tests use.
/// </summary>
internal static class RepositoryFile
{
    internal static string Path(params string[] relativeParts)
    {
        var parts = new System.Collections.Generic.List<string> { "..", "..", "..", "..", ".." };
        parts.AddRange(relativeParts);
        return System.IO.Path.GetFullPath(System.IO.Path.Combine(parts.ToArray()));
    }

    internal static byte[] Bytes(params string[] relativeParts)
    {
        var path = Path(relativeParts);
        Assert.True(File.Exists(path), $"introuvable depuis la sortie de test : {path}");
        return File.ReadAllBytes(path);
    }
}

/// <summary>
/// The BOM of builder/build.ps1 is load-bearing, not cosmetic. Windows PowerShell 5.1
/// reads a BOM-less script as Windows-1252: every multi-byte character becomes three
/// garbage ones, and some of those are curly quotes, which PowerShell honours as string
/// delimiters. A box-drawing dash in a comment once swallowed the rest of its line and
/// stripped $LASTEXITCODE out of a guard — a build that reported success over a failed
/// publish.
///
/// The failure is silent and only shows up on a release machine, which is exactly the
/// kind of fact worth pinning in the suite instead of in a comment nobody re-reads.
/// </summary>
public class BuildScriptEncodingTests
{
    private static readonly string[] ScriptPath = { "builder", "build.ps1" };

    [Fact]
    public void BuildScript_StartsWithUtf8Bom()
    {
        var bytes = RepositoryFile.Bytes(ScriptPath);
        Assert.True(bytes.Length >= 3, "builder/build.ps1 est vide ou tronque");
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
    }

    [Fact]
    public void BuildScript_IsValidUtf8()
    {
        // A strict decoder: re-encoding a mojibake'd file would succeed silently, so the
        // point is to reject byte sequences that are not UTF-8 at all.
        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false,
                                      throwOnInvalidBytes: true);
        var bytes = RepositoryFile.Bytes(ScriptPath);
        strict.GetString(bytes, 3, bytes.Length - 3);
    }

    [Fact]
    public void BuildScript_HasNoCurlyQuotes()
    {
        // The specific characters PowerShell treats as string delimiters, and the ones a
        // CP1252 round-trip produces. Their presence means the file has already been
        // damaged by an editor, whatever the BOM says.
        var text = File.ReadAllText(RepositoryFile.Path(ScriptPath));
        foreach (var quote in new[] { '‘', '’', '“', '”' })
        {
            Assert.DoesNotContain(quote.ToString(), text);
        }
    }
}

/// <summary>
/// Same class of defect, other end of the toolchain: Inno Setup 6 reads a .iss with no
/// BOM in the system ANSI code page. RiveTT.iss is UTF-8 and its wizard messages are in
/// French, so without the BOM every accent reached the user as mojibake — "installé"
/// rendered as "installÃ©" on a French Windows. Unlike the PowerShell case this one
/// never breaks the build; it just ships.
/// </summary>
public class IssEncodingTests
{
    private static readonly string[] IssPath = { "builder", "installer", "RiveTT.iss" };

    [Fact]
    public void InstallerScript_StartsWithUtf8Bom()
    {
        var bytes = RepositoryFile.Bytes(IssPath);
        Assert.True(bytes.Length >= 3, "builder/installer/RiveTT.iss est vide ou tronque");
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
    }

    [Fact]
    public void InstallerScript_IsValidUtf8()
    {
        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false,
                                      throwOnInvalidBytes: true);
        var bytes = RepositoryFile.Bytes(IssPath);
        strict.GetString(bytes, 3, bytes.Length - 3);
    }

    /// <summary>
    /// The .iss reads its payload out of builder/staging/ and nothing else. Pointing a
    /// Source at ..\..\src or ..\..\dist would work on the machine that wrote it and break
    /// the invariant that dist/ is publishable as it stands.
    /// </summary>
    [Fact]
    public void InstallerScript_ReadsOnlyFromStaging()
    {
        var text = File.ReadAllText(RepositoryFile.Path(IssPath));
        var sources = text.Split('\n')
                          .Select(line => line.TrimStart())
                          .Where(line => line.StartsWith("Source:", System.StringComparison.Ordinal))
                          .ToArray();

        Assert.NotEmpty(sources);
        foreach (var source in sources)
        {
            Assert.Contains("\"..\\staging\\", source);
        }
    }
}
