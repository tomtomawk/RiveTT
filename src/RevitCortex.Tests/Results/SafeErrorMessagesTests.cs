using RevitCortex.Core.Results;
using Xunit;

namespace RevitCortex.Tests.Results;

public class SafeErrorMessagesTests
{
    [Fact]
    public void ForInternal_NamesTheExceptionType_NotItsMessage()
    {
        var s = SafeErrorMessages.ForInternal(
            new System.IO.FileNotFoundException("C:\\Users\\mario\\secret.rvt missing"));
        Assert.Contains("FileNotFoundException", s);
        Assert.DoesNotContain("secret.rvt", s);
        Assert.DoesNotContain("mario", s);
    }
}
