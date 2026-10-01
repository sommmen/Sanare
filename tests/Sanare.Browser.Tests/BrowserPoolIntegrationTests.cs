using Microsoft.Playwright;
using Sanare.Core.Acquisition;

namespace Sanare.Browser.Tests;

[Trait("Category", "Browser")]
public sealed class BrowserPoolIntegrationTests
{
    [Fact]
    public async Task RentAsync_allows_concurrent_leases_up_to_the_configured_capacity()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var pool = new BrowserPool(playwright, new BrowserOptions(Enabled: true, MaxContexts: 2));

        await using var first = await pool.RentAsync();
        await using var second = await pool.RentAsync();

        Assert.Equal(2, pool.Statistics.Rented);
        Assert.Equal(0, pool.Statistics.Available);
        Assert.Equal(2, pool.Statistics.TotalCreated);
    }

    [Fact]
    public async Task RentAsync_fails_with_a_bounded_wait_when_pool_is_exhausted()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var pool = new BrowserPool(playwright, new BrowserOptions(
            Enabled: true,
            MaxContexts: 1,
            BrowserWaitTimeout: TimeSpan.FromMilliseconds(100)));
        await using var lease = await pool.RentAsync();

        var exception = await Assert.ThrowsAsync<AcquisitionException>(async () => await pool.RentAsync());

        Assert.Equal("SNR-BRW-002", exception.Code);
    }

    [Fact]
    public async Task SweepIdleAsync_evicts_idle_context_and_closes_its_browser()
    {
        using var playwright = await Playwright.CreateAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        await using var pool = new BrowserPool(playwright, new BrowserOptions(
            Enabled: true,
            ContextIdleTimeout: TimeSpan.FromSeconds(1)), clock);

        await using (var lease = await pool.RentAsync())
        {
            Assert.NotNull(lease.Context);
        }

        Assert.Equal(1, pool.Statistics.TotalCreated);
        clock.Advance(TimeSpan.FromSeconds(2));
        await pool.SweepIdleAsync();

        Assert.Equal(2, pool.Statistics.Available);
        Assert.Equal(0, pool.Statistics.Rented);

        await using var replacement = await pool.RentAsync();
        Assert.Equal(2, pool.Statistics.TotalCreated);
    }

    [Fact]
    public async Task DisposeAsync_stops_child_chromium_processes()
    {
        using var playwright = await Playwright.CreateAsync();
        var before = ChromiumProcessTree.CountDescendants();
        var pool = new BrowserPool(playwright, new BrowserOptions(Enabled: true));
        await using (var lease = await pool.RentAsync())
        {
            await lease.Context.NewPageAsync();
            Assert.InRange(ChromiumProcessTree.CountDescendants(), before + 1, before + 20);
        }

        await pool.DisposeAsync();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (ChromiumProcessTree.CountDescendants() != before && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        Assert.Equal(before, ChromiumProcessTree.CountDescendants());
    }
}
