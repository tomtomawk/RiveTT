using System;
using System.IO;
using RevitCortex.Core.Licensing;
using RevitCortex.Plugin.Licensing;
using Xunit;

namespace RevitCortex.Tests.Licensing;

public class FileLicenseStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public FileLicenseStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "rc-lic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "license.json");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static StoredLicenseState Sample() => new StoredLicenseState(
        "BASE64PAYLOAD.BASE64SIG",
        new DateTime(2026, 7, 8, 10, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 7, 8, 10, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var store = new FileLicenseStore(_path);
        var state = Sample();

        store.Save(state);
        var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal(state.Token, loaded!.Token);
        Assert.Equal(state.LastOnlineCheckUtc, loaded.LastOnlineCheckUtc);
        Assert.Equal(state.HighWaterMarkUtc, loaded.HighWaterMarkUtc);
    }

    [Fact]
    public void Load_MissingFile_ReturnsNull()
    {
        Assert.False(File.Exists(_path));
        Assert.Null(new FileLicenseStore(_path).Load());
    }

    [Fact]
    public void Load_CorruptFile_ReturnsNull_NeverThrows()
    {
        File.WriteAllText(_path, "{ this is not valid json ]]]");
        var store = new FileLicenseStore(_path);

        StoredLicenseState? result = null;
        var ex = Record.Exception(() => result = store.Load());

        Assert.Null(ex);
        Assert.Null(result);
    }

    [Fact]
    public void Save_CreatesMissingDirectory()
    {
        var nested = Path.Combine(_dir, "sub", "license.json");
        new FileLicenseStore(nested).Save(Sample());
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void Save_Overwrites_ExistingFileAtomically_NoLeftoverTmp()
    {
        var store = new FileLicenseStore(_path);
        store.Save(Sample());
        store.Save(new StoredLicenseState("SECOND.SIG", null,
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)));

        var loaded = store.Load();
        Assert.NotNull(loaded);
        Assert.Equal("SECOND.SIG", loaded!.Token);
        Assert.Null(loaded.LastOnlineCheckUtc);
        Assert.False(File.Exists(_path + ".tmp"));
    }

    [Fact]
    public void Load_NullLastOnlineCheck_RoundTripsAsNull()
    {
        var store = new FileLicenseStore(_path);
        store.Save(new StoredLicenseState("t", null,
            new DateTime(2026, 7, 8, 0, 0, 0, DateTimeKind.Utc)));
        Assert.Null(store.Load()!.LastOnlineCheckUtc);
    }
}
