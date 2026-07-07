using RevitCortex.Core.Telemetry;
using Xunit;

namespace RevitCortex.Tests.Telemetry;

public class MessageClassifierTests
{
    [Theory]
    [InlineData("Timeout", "anything", "timeout")]
    [InlineData("Cancelled", "anything", "cancelled")]
    [InlineData("TransactionFailed", "commit failed", "transaction_failed")]
    [InlineData("PermissionDenied", "blocked in read-only mode", "read_only_block")]
    [InlineData("PermissionDenied", "code execution disabled", "permission_denied")]
    [InlineData("Unknown", "Unhandled exception: NullReferenceException", "exception")]
    [InlineData("InvalidInput", "Parameter 'X' not found on element", "parameter_missing")]
    [InlineData("InvalidInput", "Unknown category OST_Fake", "invalid_category")]
    [InlineData("InvalidInput", "failed to parse JSON body", "parse_error")]
    [InlineData("ElementNotFound", "socket closed by bridge", "connection_failed")]
    [InlineData("InvalidInput", "something else entirely", "unknown")]
    public void Classify_MapsKnownShapes(string code, string message, string expected)
    {
        Assert.Equal(expected, MessageClassifier.Classify(code, message));
    }

    [Fact]
    public void Classify_NullInputs_ReturnsUnknown()
    {
        Assert.Equal("unknown", MessageClassifier.Classify(null, null));
    }
}
