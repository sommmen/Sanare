using System.Net;
using Sanare.Core.Acquisition;
using Sanare.Http.Discovery;
using Sanare.Http.Politeness;
using Sanare.Http.Robots;

namespace Sanare.Http.Tests.Acquisition;

public sealed class DiscoveryDocumentResolverTests
{
    private static readonly Uri Host = new("https://example.test/");

    [Fact]
    public void The_conventional_location_is_used_when_nothing_is_advertised()
    {
        Assert.Equal(new Uri("https://example.test/llms.txt"), DiscoveryDocumentResolver.Resolve(Host, ruleSet: null));
    }

    [Fact]
    public void An_advertised_document_wins_over_the_convention()
    {
        var rules = RobotsTxtParser.Parse("Sitemap: https://example.test/docs/llms.txt\n", "Sanare");

        Assert.Equal(new Uri("https://example.test/docs/llms.txt"), DiscoveryDocumentResolver.Resolve(Host, rules));
    }

    [Fact]
    public void An_ordinary_sitemap_is_not_mistaken_for_a_discovery_document()
    {
        var rules = RobotsTxtParser.Parse("Sitemap: https://example.test/sitemap.xml\n", "Sanare");

        Assert.Equal(new Uri("https://example.test/llms.txt"), DiscoveryDocumentResolver.Resolve(Host, rules));
    }

    [Fact]
    public async Task A_present_document_is_returned()
    {
        var handler = new ScriptedHandler()
            .Enqueue("https://example.test/robots.txt", body: "User-agent: *\nDisallow:\n", contentType: "text/plain")
            .Enqueue("https://example.test/llms.txt", body: "# Example\nProducts: /products", contentType: "text/plain");

        var document = await Build(handler).FetchAsync(Host, "lenovo");

        Assert.NotNull(document);
        Assert.Contains("Products", document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_document_is_non_fatal()
    {
        // SNR-ACQ-009 is a warning by design: llms.txt is a bonus, and the run must continue without it.
        var handler = new ScriptedHandler()
            .Enqueue("https://example.test/robots.txt", body: "User-agent: *\nDisallow:\n", contentType: "text/plain")
            .Enqueue("https://example.test/llms.txt", HttpStatusCode.NotFound, body: "nope", contentType: "text/plain");

        Assert.Null(await Build(handler).FetchAsync(Host, "lenovo"));
    }

    [Fact]
    public async Task A_transport_failure_is_non_fatal()
    {
        Assert.Null(await Build(new FailingHandler()).FetchAsync(Host, "lenovo"));
    }

    [Fact]
    public async Task A_document_disallowed_by_robots_is_not_fetched()
    {
        var handler = new ScriptedHandler()
            .Enqueue("https://example.test/robots.txt", body: "User-agent: *\nDisallow: /llms.txt\n", contentType: "text/plain");

        Assert.Null(await Build(handler).FetchAsync(Host, "lenovo"));
        Assert.DoesNotContain(handler.Requests, request => request.RequestUri!.AbsolutePath == "/llms.txt");
    }

    [Fact]
    public void Only_text_shaped_content_types_are_expected()
    {
        Assert.Contains("text/plain", DiscoveryDocumentResolver.ExpectedContentTypes);
        Assert.Contains("text/markdown", DiscoveryDocumentResolver.ExpectedContentTypes);
        Assert.DoesNotContain("text/html", DiscoveryDocumentResolver.ExpectedContentTypes);
    }

    private static DiscoveryDocumentResolver Build(HttpMessageHandler handler)
    {
        var clock = new FakeTimeProvider();
        var options = new AcquisitionOptions(RateLimit: new RateLimitOptions(MinHostDelay: TimeSpan.Zero));
        var client = new HttpClient(handler);
        var limiters = new HostLimiterRegistry(options, clock);
        var robots = new RobotsPolicy(client, options, limiters, clock,
            diskRoot: Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var transport = new HttpContentAcquirer(client, new RecordingFixtureCorpus(), options, clock);

        return new DiscoveryDocumentResolver(transport, robots);
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("connection reset");
    }
}
