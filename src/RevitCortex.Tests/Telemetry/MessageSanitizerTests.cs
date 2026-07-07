using RevitCortex.Core.Telemetry;
using Xunit;

namespace RevitCortex.Tests.Telemetry;

public class MessageSanitizerTests
{
    [Theory]
    [InlineData("Element 12345 does not exist in the active document",
                "Element 99 does not exist in the active document")]
    [InlineData("Failed on C:\\Projects\\TorreA\\model.rvt line 12",
                "Failed on D:\\Other\\Secret\\z.rvt line 99")]
    [InlineData("Guid 0d534e54-53c8-4f7e-a418-11ab5b58a475 invalid",
                "Guid ffffffff-aaaa-bbbb-cccc-000011112222 invalid")]
    public void Normalize_CollapsesVariants_ToSameString(string a, string b)
    {
        Assert.Equal(MessageSanitizer.Normalize(a), MessageSanitizer.Normalize(b));
    }

    [Fact]
    public void Normalize_StripsQuotedStrings_PathsGuidsNumbersEmails()
    {
        var raw = "Param 'WBS_Codice' on \"Torre A - Modello Centrale\" at C:\\Users\\mario.rossi\\file.rvt (id 606873, mario.rossi@gpapartners.com)";
        var n = MessageSanitizer.Normalize(raw);
        Assert.DoesNotContain("WBS_Codice", n);
        Assert.DoesNotContain("Torre A", n);
        Assert.DoesNotContain("mario.rossi", n);
        Assert.DoesNotContain("606873", n);
        Assert.DoesNotContain(":\\", n);
    }

    [Fact]
    public void Normalize_StripsUnquotedCompoundAndIfcTokens()
    {
        var n = MessageSanitizer.Normalize("Parameter WBS_Code missing; IfcWallStandardCase rejected");
        Assert.DoesNotContain("WBS_Code", n);
        Assert.DoesNotContain("IfcWallStandardCase", n);
    }

    [Fact]
    public void TrySanitize_TemplatedSafeMessage_ReturnsTrueWithText()
    {
        var ok = MessageSanitizer.TrySanitizeForTransmission(
            "Element 12345 does not exist in the active document", out var s);
        Assert.True(ok);
        Assert.Contains("does not exist in the active document", s);
        Assert.DoesNotContain("12345", s);
    }

    [Theory]
    [InlineData("Cannot open \\\\server\\share\\proj.rvt")]
    [InlineData("User luigi.dattilo@gpapartners.com not authorized")]
    public void TrySanitize_ResidualSuspiciousContent_FailsClosed(string raw)
    {
        // These are crafted so a residue survives stripping (regex gaps are
        // expected in the wild — the verdict must fail closed, not leak).
        var mutated = raw.Replace("\\\\", "\\ \\").Replace("@", " @ ");
        var ok = MessageSanitizer.TrySanitizeForTransmission(mutated, out _);
        Assert.False(ok);
    }

    [Fact]
    public void TrySanitize_EmptyOrNull_FailsClosed()
    {
        Assert.False(MessageSanitizer.TrySanitizeForTransmission(null, out _));
        Assert.False(MessageSanitizer.TrySanitizeForTransmission("   ", out _));
    }

    [Fact]
    public void TrySanitize_CapsAt200Chars()
    {
        var ok = MessageSanitizer.TrySanitizeForTransmission(new string('a', 500), out var s);
        Assert.True(ok);
        Assert.True(s.Length <= 200);
    }
}
