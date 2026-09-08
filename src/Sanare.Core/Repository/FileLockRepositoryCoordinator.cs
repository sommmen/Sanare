namespace Sanare.Core.Repository;

/// <summary>
/// Default single-machine <see cref="IRepositoryCoordinator"/>: an inter-process lock file at
/// <c>{StateRoot}/scripts/.sanare-lock</c>. See docs/features/script-repository.md ("Repository coordination and integrity").
/// </summary>
public sealed class FileLockRepositoryCoordinator(string lockFilePath) : IRepositoryCoordinator
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);

    public async ValueTask<IAsyncDisposable> AcquireWriteLeaseAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        var directory = Path.GetDirectoryName(lockFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var stream = new FileStream(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return new Lease(stream);
            }
            catch (IOException)
            {
                if (DateTimeOffset.UtcNow >= deadline)
                {
                    throw new ScriptRepositoryException("SNR-GIT-004", "Timed out acquiring the script-repository write lease.", retryable: true);
                }

                await Task.Delay(RetryDelay, ct).ConfigureAwait(false);
            }
        }
    }

    private sealed class Lease(FileStream stream) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            stream.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
