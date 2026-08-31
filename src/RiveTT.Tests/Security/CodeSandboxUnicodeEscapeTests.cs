using RiveTT.Core.Security;
using Xunit;

namespace RiveTT.Tests.Security;

/// <summary>
/// Unicode escapes in identifiers. C# lets any identifier letter be spelled \uXXXX, so
/// this source:
///
///     System.\u0049O.F\u0069le.WriteAllText(p, s);
///
/// is read by the compiler as System.IO.File.WriteAllText and was read by the matcher
/// as neither "System.IO" nor "File." — measured ALLOWED on 2026-08-31.
///
/// Every snippet below is a VERBATIM string (@"..."): in a normal literal the C#
/// compiler would decode the escapes itself and hand Validate the plain form, and the
/// test would pass without exercising the decoder at all.
///
/// Assertions, not probes — CodeSandboxBypassExplorationTests documents what the
/// sandbox currently does; this file pins what must not come back.
/// </summary>
public class CodeSandboxUnicodeEscapeTests
{
    [Fact]
    public void EscapedNamespaceAndType_IsBlockedLikeThePlainForm()
    {
        Assert.NotNull(CodeSandboxV2.Validate(@"System.IO.File.WriteAllText(p, s);"));
        Assert.NotNull(CodeSandboxV2.Validate(@"System.\u0049O.F\u0069le.WriteAllText(p, s);"));
    }

    [Fact]
    public void EscapedAlias_IsBlocked()
    {
        // The alias hides the namespace, and the escapes hid the alias target.
        Assert.NotNull(CodeSandboxV2.Validate(
            @"using SIO = System.\u0049O; SIO.F\u0069le.WriteAllText(p, s);"));
    }

    [Fact]
    public void EscapedProcessStart_IsBlocked()
    {
        Assert.NotNull(CodeSandboxV2.Validate(@"\u0050rocess.Start(""cmd.exe"");"));
    }

    [Fact]
    public void EscapedReflectionWalk_IsBlocked()
    {
        Assert.NotNull(CodeSandboxV2.Validate(
            @"var t = \u0041ctivator.CreateInstance(someType);"));
    }

    [Fact]
    public void Decoder_ProducesTheIdentifierTheCompilerSees()
    {
        Assert.Equal("System.IO.File",
            CodeSandboxV2.DecodeIdentifierEscapes(@"System.\u0049O.F\u0069le"));
    }

    [Fact]
    public void DecodingDoesNotBreakLegitimateRevitCode()
    {
        // The decoder runs on every snippet, the passing ones included. A false positive
        // here blocks normal use, which is how a sandbox gets switched off instead of fixed.
        Assert.Null(CodeSandboxV2.Validate(
            @"var walls = new FilteredElementCollector(document)
                  .OfClass(typeof(Wall)).ToElements(); return walls.Count;"));

        Assert.Null(CodeSandboxV2.Validate(@"var label = ""niveau R+1""; return label.Length;"));
    }

    [Fact]
    public void MalformedEscape_IsLeftAlone()
    {
        // Not four hex digits: not an escape. The decoder must neither eat it nor throw.
        Assert.Equal(@"a\uZZZZb", CodeSandboxV2.DecodeIdentifierEscapes(@"a\uZZZZb"));
        Assert.Equal(@"trailing\u00", CodeSandboxV2.DecodeIdentifierEscapes(@"trailing\u00"));
    }

    [Fact]
    public void LoneSurrogate_IsNotDecoded()
    {
        // char.ConvertFromUtf32 throws on a lone surrogate; declining beats taking the
        // whole validation path down with it.
        Assert.Equal(@"x\uD800y", CodeSandboxV2.DecodeIdentifierEscapes(@"x\uD800y"));
    }
}
