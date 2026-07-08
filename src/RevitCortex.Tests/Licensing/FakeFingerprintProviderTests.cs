using System.Collections.Generic;
using RevitCortex.Core.Licensing;
using Xunit;

namespace RevitCortex.Tests.Licensing;

public class FakeFingerprintProviderTests
{
    [Fact]
    public void ReturnsConfiguredHashes()
    {
        var provider = new FakeFingerprintProvider(new[] { "h1", "h2" });
        Assert.Equal(new[] { "h1", "h2" }, provider.GetHashedAttributes());
    }

    [Fact]
    public void EmptyByDefault_NeverNull()
    {
        var hashes = new FakeFingerprintProvider().GetHashedAttributes();
        Assert.NotNull(hashes);
        Assert.Empty(hashes);
    }

    [Fact]
    public void ReturnsReadOnlyListContract()
    {
        IFingerprintProvider provider = new FakeFingerprintProvider(new List<string> { "x" });
        IReadOnlyList<string> hashes = provider.GetHashedAttributes();
        Assert.Single(hashes);
        Assert.Equal("x", hashes[0]);
    }
}
