using RevitCortex.Core.Telemetry;
using Xunit;

namespace RevitCortex.Tests.Telemetry;

public class ErrorFingerprinterTests
{
    [Fact]
    public void SameBug_DifferentElementIds_SameFingerprint()
    {
        var a = Fp("Element 12345 does not exist");
        var b = Fp("Element 99 does not exist");
        Assert.Equal(a, b);
    }

    [Fact]
    public void DifferentTool_DifferentFingerprint()
    {
        var a = ErrorFingerprinter.Compute("tool_a", "InvalidInput", "tool", "unknown",
            MessageSanitizer.Normalize("x"));
        var b = ErrorFingerprinter.Compute("tool_b", "InvalidInput", "tool", "unknown",
            MessageSanitizer.Normalize("x"));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Fingerprint_Is16LowercaseHexChars()
    {
        var f = Fp("anything");
        Assert.Matches("^[0-9a-f]{16}$", f);
    }

    private static string Fp(string message) =>
        ErrorFingerprinter.Compute("create_dimensions", "InvalidInput", "tool",
            "parameter_missing", MessageSanitizer.Normalize(message));
}
