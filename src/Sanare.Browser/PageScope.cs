using Microsoft.Playwright;

namespace Sanare.Browser;

/// <summary>
/// Opens a page from a leased context, runs a delegate against it, and guarantees the page is
/// closed on every exit path (docs/features/browser-tier.md, T5 "page-scoped execution / leak
/// safety"). The lease itself is owned by the caller and is not disposed here.
/// </summary>
public sealed class PageScope
{
    private readonly BrowserPool? _pool;

    public PageScope(BrowserPool? pool = null) => _pool = pool;

    public async ValueTask<TResult> RunAsync<TResult>(
        IBrowserLease lease,
        Func<IPage, ValueTask<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(action);

        var page = await lease.Context.NewPageAsync().ConfigureAwait(false);
        _pool?.PageOpened();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await action(page).ConfigureAwait(false);
        }
        catch
        {
            lease.MarkFaulted();
            throw;
        }
        finally
        {
            try
            {
                await page.CloseAsync().ConfigureAwait(false);
            }
            finally
            {
                _pool?.PageClosed();
            }
        }
    }
}
