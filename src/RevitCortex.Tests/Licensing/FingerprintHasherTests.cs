using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using RevitCortex.Plugin.Licensing;
using Xunit;

namespace RevitCortex.Tests.Licensing;

public class FingerprintHasherTests
{
    private static string Sha256Hex(string input)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    [Fact]
    public void Hash_HashesEachValueWithSha256Hex_InOrder()
    {
        var result = FingerprintHasher.Hash(new[] { "ABC-123", "SN-999" });
        Assert.Equal(2, result.Count);
        Assert.Equal(Sha256Hex("ABC-123"), result[0]);
        Assert.Equal(Sha256Hex("SN-999"), result[1]);
    }

    [Fact]
    public void Hash_OmitsNullEmptyAndWhitespaceValues()
    {
        var result = FingerprintHasher.Hash(new[] { "GUID", null, "", "   " });
        Assert.Single(result);
        Assert.Equal(Sha256Hex("GUID"), result[0]);
    }

    [Fact]
    public void Hash_EmptyInput_ReturnsEmptyList()
    {
        Assert.Empty(FingerprintHasher.Hash(new string?[0]));
    }

    [Fact]
    public void Hash_NullInput_ReturnsEmptyList_NeverThrows()
    {
        Assert.Empty(FingerprintHasher.Hash(null));
    }

    [Fact]
    public void Hash_ProducesLowercase64CharHex()
    {
        var result = FingerprintHasher.Hash(new[] { "anything" });
        Assert.Single(result);
        Assert.Equal(64, result[0].Length);
        Assert.Equal(result[0].ToLowerInvariant(), result[0]);
    }
}

public class WindowsFingerprintProviderContractTests
{
    [RequiresMachineGuidFact]
    public void GetHashedAttributes_IncludesMachineGuidHash_OnRealMachine()
    {
        var hashes = new WindowsFingerprintProvider().GetHashedAttributes();
        Assert.NotEmpty(hashes);
        Assert.Equal(64, hashes[0].Length); // SHA-256 hex
    }

    [Fact]
    public void GetHashedAttributes_NeverThrows()
    {
        var ex = Record.Exception(() => new WindowsFingerprintProvider().GetHashedAttributes());
        Assert.Null(ex);
    }
}
