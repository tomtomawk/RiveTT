using System;
using RevitCortex.Core.Licensing;
using Xunit;

namespace RevitCortex.Tests.Licensing;

public class SystemClockTests
{
    [Fact]
    public void SystemClock_ReturnsUtcNow_KindUtc()
    {
        Assert.Equal(DateTimeKind.Utc, new SystemClock().UtcNow.Kind);
    }

    [Fact]
    public void SystemClock_IsMonotonicAcrossReads()
    {
        var clock = new SystemClock();
        var a = clock.UtcNow;
        var b = clock.UtcNow;
        Assert.True(b >= a);
    }

    [Fact]
    public void TestClock_ReturnsFixedValue()
    {
        var fixedNow = new DateTime(2026, 5, 4, 3, 2, 1, DateTimeKind.Utc);
        Assert.Equal(fixedNow, new TestClock(fixedNow).UtcNow);
    }

    [Fact]
    public void TestClock_AdvanceMovesTimeForward()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var clock = new TestClock(start);
        clock.Advance(TimeSpan.FromHours(48));
        Assert.Equal(start.AddHours(48), clock.UtcNow);
    }

    [Fact]
    public void TestClock_SetOverridesTime()
    {
        var clock = new TestClock(DateTime.UtcNow);
        var target = new DateTime(2030, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        clock.Set(target);
        Assert.Equal(target, clock.UtcNow);
    }
}
