using System;

namespace RevitCortex.Core.Licensing;

/// <summary>Abstraction over wall-clock time: deterministic tests + anti-rollback.</summary>
public interface ISystemClock
{
    DateTime UtcNow { get; }
}

/// <summary>Real clock. Always returns a UTC-kind timestamp.</summary>
public class SystemClock : ISystemClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>Mutable clock for tests: fixed, advanceable, settable.</summary>
public class TestClock : ISystemClock
{
    private DateTime _now;

    public TestClock(DateTime now)
    {
        _now = DateTime.SpecifyKind(now, DateTimeKind.Utc);
    }

    public DateTime UtcNow => _now;

    public void Advance(TimeSpan delta) => _now = _now.Add(delta);

    public void Set(DateTime now) => _now = DateTime.SpecifyKind(now, DateTimeKind.Utc);
}
