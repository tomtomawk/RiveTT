using System;
using RevitCortex.Core.Licensing;
using Xunit;

namespace RevitCortex.Tests.Licensing;

public class InMemoryLicenseStoreTests
{
    [Fact]
    public void Load_ReturnsNull_WhenNothingSaved()
    {
        Assert.Null(new InMemoryLicenseStore().Load());
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var store = new InMemoryLicenseStore();
        var check = new DateTime(2026, 7, 8, 10, 0, 0, DateTimeKind.Utc);
        var hwm = new DateTime(2026, 7, 8, 10, 0, 0, DateTimeKind.Utc);

        store.Save(new StoredLicenseState("base64-token", check, hwm));

        var loaded = store.Load();
        Assert.NotNull(loaded);
        Assert.Equal("base64-token", loaded!.Token);
        Assert.Equal(check, loaded.LastOnlineCheckUtc);
        Assert.Equal(hwm, loaded.HighWaterMarkUtc);
    }

    [Fact]
    public void Save_OverwritesPreviousState()
    {
        var store = new InMemoryLicenseStore();
        store.Save(new StoredLicenseState("t1", DateTime.UtcNow, DateTime.UtcNow));
        store.Save(new StoredLicenseState("t2", DateTime.UtcNow, DateTime.UtcNow));

        Assert.Equal("t2", store.Load()!.Token);
    }

    [Fact]
    public void StoredLicenseState_AllowsNullLastOnlineCheck()
    {
        var hwm = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var s = new StoredLicenseState("tok", null, hwm);

        Assert.Equal("tok", s.Token);
        Assert.Null(s.LastOnlineCheckUtc);
        Assert.Equal(hwm, s.HighWaterMarkUtc);
    }
}
