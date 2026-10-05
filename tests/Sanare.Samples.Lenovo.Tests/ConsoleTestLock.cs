namespace Sanare.Samples.Lenovo.Tests;

/// <summary>
/// Tests across this assembly redirect the shared, process-wide <see cref="Console.Out"/> and
/// <see cref="Console.Error"/>. xUnit runs test classes in parallel by default, so every test that swaps
/// console streams must serialize on this single lock, not a per-class one, or two tests can race on the
/// same console state.
/// </summary>
internal static class ConsoleTestLock
{
    public static readonly Lock Instance = new();
}
