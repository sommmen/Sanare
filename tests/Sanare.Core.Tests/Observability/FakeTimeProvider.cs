namespace Sanare.Core.Tests.Observability;

/// <summary>A deterministic <see cref="TimeProvider"/> for tests that assert time-based rules.</summary>
public sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now += delta;

    public void SetTo(DateTimeOffset value) => _now = value;
}
