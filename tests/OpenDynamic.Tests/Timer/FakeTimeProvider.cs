namespace OpenDynamic.Tests.Timer;

/// <summary>
/// Controllable <see cref="TimeProvider"/> implementation for deterministic time testing.
/// </summary>
public sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public FakeTimeProvider(DateTimeOffset? initialTime = null)
    {
        _utcNow = initialTime ?? new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan delta)
    {
        _utcNow = _utcNow.Add(delta);
    }

    public void SetUtcNow(DateTimeOffset time)
    {
        _utcNow = time;
    }
}
