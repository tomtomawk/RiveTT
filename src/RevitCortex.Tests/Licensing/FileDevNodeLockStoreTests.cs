using System;
using System.IO;
using RevitCortex.Plugin.Licensing;
using Xunit;

namespace RevitCortex.Tests.Licensing;

public class FileDevNodeLockStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public FileDevNodeLockStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "rc-nodelock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "dev-node-lock.json");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void GetBoundFingerprint_Unknown_ReturnsNull()
        => Assert.Null(new FileDevNodeLockStore(_path).GetBoundFingerprint("K"));

    [Fact]
    public void TryBind_ThenGet_ReturnsFingerprint()
    {
        var s = new FileDevNodeLockStore(_path);
        Assert.True(s.TryBind("K", "fp1"));
        Assert.Equal("fp1", s.GetBoundFingerprint("K"));
    }

    [Fact]
    public void TryBind_PersistsAcrossInstances()
    {
        new FileDevNodeLockStore(_path).TryBind("K", "fp1");
        Assert.Equal("fp1", new FileDevNodeLockStore(_path).GetBoundFingerprint("K"));
    }

    [Fact]
    public void CorruptFile_TreatedAsEmpty()
    {
        File.WriteAllText(_path, "{ not json ");
        Assert.Null(new FileDevNodeLockStore(_path).GetBoundFingerprint("K"));
    }
}
