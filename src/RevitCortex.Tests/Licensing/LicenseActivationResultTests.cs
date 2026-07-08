using RevitCortex.Core.Licensing;
using Xunit;

namespace RevitCortex.Tests.Licensing;

public class LicenseActivationResultTests
{
    [Fact]
    public void Ok_CarriesTokenAndSuccess_NoError()
    {
        var r = LicenseActivationResult.Ok("signed-token-abc");
        Assert.True(r.Success);
        Assert.Equal("signed-token-abc", r.Token);
        Assert.Null(r.Error);
    }

    [Fact]
    public void Fail_CarriesError_NoToken()
    {
        var r = LicenseActivationResult.Fail("invalid license key");
        Assert.False(r.Success);
        Assert.Null(r.Token);
        Assert.Equal("invalid license key", r.Error);
    }
}
