using System.Net;
using Sanare.Core.Acquisition;
using Sanare.Http.Identity;
using Sanare.Http.Politeness;
using Sanare.Http.Robots;

namespace Sanare.Http.Tests.Acquisition;

public sealed class GovernedContentAcquirerTests
{
    private const string Page = "https://example.test/tablets";
    private const string RobotsUrl = "https://example.test/robots.txt";

    [Fact]
    public async Task A_disallowed_path_fails_without_reaching_the_page()
    {
        // AC-011: the stub must record zero requests for the disallowed path.
        var handler = new ScriptedHandler()
            .Enqueue(RobotsUrl, body: "User-agent: *\nDisallow: /tablets\n", contentType: "text/plain");

        var (acquirer, _) = Build(handler);

        var exception = await Assert.ThrowsAsync<AcquisitionException>(
            async () => await acquirer.AcquireAsync(new AcquisitionRequest(new Uri(Page), "lenovo")));

        Assert.Equal("SNR-ACQ-004", exception.Code);
        Assert.DoesNotContain(handler.Requests, request => request.RequestUri!.AbsoluteUri == Page);
    }

    [Fact]
    public async Task An_allowed_path_is_fetched()
    {
        var handler = new ScriptedHandler()
            .Enqueue(RobotsUrl, body: "User-agent: *\nDisallow: /admin\n", contentType: "text/plain")
            .Enqueue(Page);

        var (acquirer, _) = Build(handler);
        var content = await acquirer.AcquireAsync(new AcquisitionRequest(new Uri(Page), "lenovo"));

        Assert.Equal(200, content.StatusCode);
        Assert.Equal(ContentOrigin.Network, content.Origin);
    }

    [Fact]
    public async Task A_missing_robots_file_grants_access()
    {
        // AC-011b: a 404 robots.txt means "no restrictions", not "deny everything".
        var handler = new ScriptedHandler()
            .Enqueue(RobotsUrl, HttpStatusCode.NotFound, body: "nope", contentType: "text/plain")
            .Enqueue(Page);

        var (acquirer, _) = Build(handler);
        var content = await acquirer.AcquireAsync(new AcquisitionRequest(new Uri(Page), "lenovo"));

        Assert.Equal(200, content.StatusCode);
    }

    [Fact]
    public async Task Stealth_mode_proceeds_past_a_disallow()
    {
        // AC-011a: stealth is explicit and audited, and still runs the rest of the governed path.
        var handler = new ScriptedHandler()
            .Enqueue(RobotsUrl, body: "User-agent: *\nDisallow: /tablets\n", contentType: "text/plain")
            .Enqueue(Page);

        var (acquirer, _) = Build(handler, mode: AcquisitionMode.Stealth);
        var content = await acquirer.AcquireAsync(new AcquisitionRequest(new Uri(Page), "lenovo"));

        Assert.Equal(200, content.StatusCode);
    }

    [Fact]
    public async Task A_forbidden_response_is_not_retried()
    {
        var handler = new ScriptedHandler()
            .Enqueue(RobotsUrl, body: "User-agent: *\nDisallow:\n", contentType: "text/plain")
            .Enqueue(Page, HttpStatusCode.Forbidden, body: "denied");

        var (acquirer, _) = Build(handler);

        var exception = await Assert.ThrowsAsync<AcquisitionException>(
            async () => await acquirer.AcquireAsync(new AcquisitionRequest(new Uri(Page), "lenovo")));

        Assert.Equal("SNR-ACQ-003", exception.Code);
        Assert.Single(handler.Requests, request => request.RequestUri!.AbsoluteUri == Page);
    }

    [Fact]
    public async Task Error_bodies_are_still_offered_to_the_corpus()
    {
        // A challenge page is the single most useful artefact a healing run can have; a non-2xx status
        // must not cause it to be discarded.
        var handler = new ScriptedHandler()
            .Enqueue(RobotsUrl, body: "User-agent: *\nDisallow:\n", contentType: "text/plain")
            .Enqueue(Page, HttpStatusCode.Forbidden, body: "<html>denied</html>");

        var (acquirer, corpus) = Build(handler);

        await Assert.ThrowsAsync<AcquisitionException>(
            async () => await acquirer.AcquireAsync(new AcquisitionRequest(new Uri(Page), "lenovo")));

        Assert.Contains(corpus.Captured, capture => capture.Url == Page);
    }

    [Fact]
    public async Task Offline_mode_opens_no_socket()
    {
        // AC-012: the handler throws on contact, so any socket attempt fails the test loudly.
        var handler = new ThrowingHandler();
        var (acquirer, _) = Build(handler, options: new AcquisitionOptions(Offline: true));

        await Assert.ThrowsAnyAsync<Exception>(
            async () => await acquirer.AcquireAsync(new AcquisitionRequest(new Uri(Page), "lenovo")));

        Assert.False(handler.WasContacted);
    }

    [Fact]
    public async Task A_crawl_delay_one_acquirer_learns_binds_every_acquirer_sharing_the_registry()
    {
        // AC-027: concurrent runs against one host share a single budget, so they cannot
        // collectively exceed the pace the operator (or the site) asked for.
        var options = new AcquisitionOptions(
            RateLimit: new RateLimitOptions(MinHostDelay: TimeSpan.Zero),
            Cache: new CacheOptions(Enabled: false));

        var clock = new FakeTimeProvider();
        await using var limiters = new HostLimiterRegistry(options, clock);

        Assert.Equal(TimeSpan.Zero, limiters.GetBudget("example.test").MinimumDelay);

        // The page is disallowed, so this run stops at robots.txt: the crawl delay is
        // recorded without any paced fetch that a frozen clock could never release.
        var first = BuildOver(limiters, options, clock, new ScriptedHandler()
            .Enqueue(RobotsUrl, body: "User-agent: *\nCrawl-delay: 7\nDisallow: /tablets\n", contentType: "text/plain"));

        await Assert.ThrowsAsync<AcquisitionException>(
            () => first.AcquireAsync(new AcquisitionRequest(new Uri(Page), "lenovo")).AsTask());

        // A second acquirer that never fetched robots.txt is bound by what the first one learned,
        // because both resolve the very same budget instance out of the shared registry.
        var second = BuildOver(limiters, options, clock, new ScriptedHandler());
        Assert.NotNull(second);

        Assert.Equal(TimeSpan.FromSeconds(7), limiters.GetBudget("example.test").MinimumDelay);
        Assert.Same(limiters.GetBudget("example.test"), limiters.GetBudget("EXAMPLE.TEST"));
    }

    private static IContentAcquirer BuildOver(
        HostLimiterRegistry limiters,
        AcquisitionOptions options,
        TimeProvider clock,
        HttpMessageHandler handler)
    {
        var client = new HttpClient(handler);
        var robots = new RobotsPolicy(client, options, limiters, clock, diskRoot: Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var transport = new HttpContentAcquirer(client, new RecordingFixtureCorpus(), options, clock);
        return new GovernedContentAcquirer(transport, limiters, robots, options, AcquisitionMode.Compliance, cache: null, clock: clock);
    }

    private static (IContentAcquirer Acquirer, RecordingFixtureCorpus Corpus) Build(
        HttpMessageHandler handler,
        AcquisitionOptions? options = null,
        AcquisitionMode mode = AcquisitionMode.Compliance)
    {
        var resolved = options ?? new AcquisitionOptions(
            RateLimit: new RateLimitOptions(MinHostDelay: TimeSpan.Zero),
            Cache: new CacheOptions(Enabled: false));

        var clock = new FakeTimeProvider();
        var client = new HttpClient(handler);
        var corpus = new RecordingFixtureCorpus();
        var limiters = new HostLimiterRegistry(resolved, clock);
        var robots = new RobotsPolicy(client, resolved, limiters, clock, diskRoot: Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var transport = new HttpContentAcquirer(client, corpus, resolved, clock);

        return (new GovernedContentAcquirer(transport, limiters, robots, resolved, mode, cache: null, clock: clock), corpus);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        public bool WasContacted { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            WasContacted = true;
            throw new HttpRequestException("Offline mode must not open a socket.");
        }
    }
}
