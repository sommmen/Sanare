namespace Sanare.Core.Repository;

/// <summary>
/// Write-lease abstraction guarding script-repository mutations (DR-013). The only implementation in
/// this slice is <see cref="FileLockRepositoryCoordinator"/> (single-machine); a host may substitute a
/// distributed implementation without <see cref="IScriptRepository"/> changing. See
/// docs/features/script-repository.md ("Repository coordination and integrity").
/// </summary>
public interface IRepositoryCoordinator
{
    /// <summary>
    /// Acquires an exclusive write lease, retrying at a fixed delay until <paramref name="timeout"/> elapses.
    /// Disposing the returned lease releases it.
    /// </summary>
    ValueTask<IAsyncDisposable> AcquireWriteLeaseAsync(TimeSpan timeout, CancellationToken ct = default);
}
