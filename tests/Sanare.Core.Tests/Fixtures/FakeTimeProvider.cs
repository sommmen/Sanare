namespace Sanare.Core.Tests.Fixtures;

/// <summary>Deterministic <see cref="TimeProvider"/> so fixture ids and timestamps are stable in tests.</summary>
internal sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now += delta;
}
