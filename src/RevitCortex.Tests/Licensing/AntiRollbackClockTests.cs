using System;
using RevitCortex.Plugin.Licensing;
using Xunit;

namespace RevitCortex.Tests.Licensing;

public class AntiRollbackClockTests
{
    private sealed class FakeHwmStore : IHighWaterMarkStore
    {
        public DateTime? Value;
        public int WriteCount;
        public DateTime? Read() => Value;
        public void Write(DateTime utc) { Value = utc; WriteCount++; }
    }

    private sealed class ThrowingHwmStore : IHighWaterMarkStore
    {
        public DateTime? Read() => throw new InvalidOperationException("blocked");
        public void Write(DateTime utc) => throw new InvalidOperationException("blocked");
    }

    private static DateTime Utc(int y, int mo, int d, int h = 0, int mi = 0) =>
        new DateTime(y, mo, d, h, mi, 0, DateTimeKind.Utc);

    [Fact]
    public void HighWaterMark_IsMaxOfNow_Hkcu_AndProgramData()
    {
        var now = Utc(2026, 7, 8, 12, 0);
        var hkcu = new FakeHwmStore { Value = Utc(2026, 7, 10) };   // ahead
        var pd = new FakeHwmStore { Value = Utc(2026, 7, 9) };      // between

        var clock = new AntiRollbackClock(() => now, hkcu, pd);

        Assert.Equal(Utc(2026, 7, 10), clock.HighWaterMarkUtc);
        Assert.Equal(now, clock.UtcNow);
    }

    [Fact]
    public void HighWaterMark_UsesNow_WhenBothSourcesEmptyOrBehind()
    {
        var now = Utc(2026, 7, 8, 12, 0);
        var clock = new AntiRollbackClock(() => now,
            new FakeHwmStore { Value = null }, new FakeHwmStore { Value = null });
        Assert.Equal(now, clock.HighWaterMarkUtc);
    }

    [Fact]
    public void Construction_PersistsNewMax_ToBothStores()
    {
        var now = Utc(2026, 7, 12);
        var hkcu = new FakeHwmStore { Value = Utc(2026, 7, 10) };
        var pd = new FakeHwmStore { Value = Utc(2026, 7, 9) };

        var clock = new AntiRollbackClock(() => now, hkcu, pd);

        Assert.Equal(now, clock.HighWaterMarkUtc);
        Assert.Equal(now, hkcu.Value);
        Assert.Equal(now, pd.Value);
        Assert.Equal(1, hkcu.WriteCount);
        Assert.Equal(1, pd.WriteCount);
    }

    // fix #14: a store already at/above the max is not rewritten.
    [Fact]
    public void RegistryAlreadyAhead_DoesNotRewrite()
    {
        var now = Utc(2026, 7, 5);
        var hkcu = new FakeHwmStore { Value = Utc(2026, 7, 10) };  // already ahead
        var pd = new FakeHwmStore { Value = Utc(2026, 7, 10) };    // also at max

        var clock = new AntiRollbackClock(() => now, hkcu, pd);

        Assert.Equal(Utc(2026, 7, 10), clock.HighWaterMarkUtc);
        Assert.Equal(0, hkcu.WriteCount);
        Assert.Equal(0, pd.WriteCount);
    }

    [Fact]
    public void Rollback_HighWaterMarkStaysAtMaxSeen_NotNow()
    {
        var now = Utc(2026, 7, 5);              // rolled BACK
        var hkcu = new FakeHwmStore { Value = Utc(2026, 7, 10) };
        var pd = new FakeHwmStore { Value = Utc(2026, 7, 8) };

        var clock = new AntiRollbackClock(() => now, hkcu, pd);

        Assert.Equal(Utc(2026, 7, 10), clock.HighWaterMarkUtc);
        Assert.Equal(now, clock.UtcNow);
        Assert.Equal(Utc(2026, 7, 10), hkcu.Value); // not overwritten downward
    }

    [Fact]
    public void StoreReadFailure_DoesNotThrow_FallsBackToOtherSources()
    {
        var now = Utc(2026, 7, 8);
        var pd = new FakeHwmStore { Value = Utc(2026, 7, 9) };

        AntiRollbackClock? clock = null;
        var ex = Record.Exception(() => clock = new AntiRollbackClock(() => now, new ThrowingHwmStore(), pd));

        Assert.Null(ex);
        Assert.NotNull(clock);
        Assert.Equal(Utc(2026, 7, 9), clock!.HighWaterMarkUtc);
    }
}
