using RevitCortex.Core.Licensing;
using Xunit;

namespace RevitCortex.Tests.Licensing;

public class LicenseStateTests
{
    [Fact]
    public void Enum_HasExactlyFiveMembers()
    {
        Assert.Equal(5, System.Enum.GetNames(typeof(LicenseState)).Length);
    }

    [Theory]
    [InlineData("Active")]
    [InlineData("Trial")]
    [InlineData("Grace")]
    [InlineData("Expired")]
    [InlineData("Invalid")]
    public void Enum_DefinesExpectedMember(string member)
    {
        Assert.True(System.Enum.IsDefined(typeof(LicenseState), member));
    }

    [Fact]
    public void Invalid_IsDefaultValue()
    {
        Assert.Equal(LicenseState.Invalid, default(LicenseState));
    }

    [Fact]
    public void NumericValues_ArePinned()
    {
        Assert.Equal(0, (int)LicenseState.Invalid);
        Assert.Equal(1, (int)LicenseState.Expired);
        Assert.Equal(2, (int)LicenseState.Grace);
        Assert.Equal(3, (int)LicenseState.Trial);
        Assert.Equal(4, (int)LicenseState.Active);
    }
}
