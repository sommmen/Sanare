using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Sanare.Abstractions;
using Sanare.Core.Acquisition;
using Sanare.Core.Fixtures;

namespace Sanare.Core.Tests.Acquisition;

public sealed class HttpContentAcquirerTests
{
    [Fact]
    public async Task AcquireAsync_replays_matching_fixture_without_sending_a_network_request()
    {
        var corpus = new TestFixtureCorpus();
        var url = new Uri("https://example.test/product");
        corpus.Add("source", url, "product", "<main>replayed</main>");
        var handler = new StubHandler(_ => throw new InvalidOperationException("Network must not be used while offline."));
        var acquirer = new HttpContentAcquirer(new HttpClient(handler), corpus, new AcquisitionOptions(Offline: true));

        var content = await acquirer.AcquireAsync(new AcquisitionRequest(url, "source", PageRole: "product"));

        Assert.Equal(ContentOrigin.Fixture, content.Origin);
        Assert.Equal("<main>replayed</main>", Encoding.UTF8.GetString(content.Body.Span));
        Assert.Equal(0, handler.SendCount);
    }

    [Fact]
    public async Task AcquireAsync_uses_the_charset_from_the_content_type_header()
    {
        var body = Encoding.Unicode.GetBytes("<main>café</main>");
        var handler = new StubHandler(_ => CreateResponse(body, "text/html", "utf-16"));
        var acquirer = new HttpContentAcquirer(new HttpClient(handler), new TestFixtureCorpus());

        var content = await acquirer.AcquireAsync(new AcquisitionRequest(new Uri("https://example.test/product"), "source"));

        Assert.Equal(Encoding.Unicode.WebName, content.Charset.WebName);
        Assert.Equal("<main>café</main>", content.Charset.GetString(content.Body.Span));
    }

    [Fact]
    public async Task AcquireAsync_uses_a_bom_before_a_meta_charset_declaration()
    {
        var body = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("<meta charset=\"utf-8\"><main>café</main>")).ToArray();
        var handler = new StubHandler(_ => CreateResponse(body, "text/html"));
        var acquirer = new HttpContentAcquirer(new HttpClient(handler), new TestFixtureCorpus());

        var content = await acquirer.AcquireAsync(new AcquisitionRequest(new Uri("https://example.test/product"), "source"));

        Assert.Equal(Encoding.Unicode.WebName, content.Charset.WebName);
    }

    [Fact]
    public async Task AcquireAsync_uses_an_html_meta_charset_when_no_header_or_bom_is_available()
    {
        var body = Encoding.Latin1.GetBytes("<meta charset=\"iso-8859-1\"><main>café</main>");
        var handler = new StubHandler(_ => CreateResponse(body, "text/html"));
        var acquirer = new HttpContentAcquirer(new HttpClient(handler), new TestFixtureCorpus());

        var content = await acquirer.AcquireAsync(new AcquisitionRequest(new Uri("https://example.test/product"), "source"));

        Assert.Equal(Encoding.Latin1.WebName, content.Charset.WebName);
        Assert.Equal("<meta charset=\"iso-8859-1\"><main>café</main>", content.Charset.GetString(content.Body.Span));
    }

    [Fact]
    public async Task AcquireAsync_captures_successful_network_content()
    {
        var corpus = new TestFixtureCorpus();
        var url = new Uri("https://example.test/product");
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<main>network</main>", Encoding.UTF8, "text/html"),
        });
        var acquirer = new HttpContentAcquirer(new HttpClient(handler), corpus);

        var content = await acquirer.AcquireAsync(new AcquisitionRequest(url, "source", PageRole: "product"));

        Assert.Equal(ContentOrigin.Network, content.Origin);
        Assert.Equal("<main>network</main>", Encoding.UTF8.GetString(content.Body.Span));
        Assert.Single(corpus.Captured);
        Assert.Equal(url.AbsoluteUri, corpus.Captured[0].Url);
    }

    [Fact]
    public async Task AcquireAsync_captures_an_unexpected_content_type_before_rejecting_it()
    {
        var corpus = new TestFixtureCorpus();
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"name\":\"product\"}", Encoding.UTF8, "application/json"),
        });
        var acquirer = new HttpContentAcquirer(new HttpClient(handler), corpus);

        var exception = await Assert.ThrowsAsync<AcquisitionException>(() => acquirer.AcquireAsync(new AcquisitionRequest(new Uri("https://example.test/product"), "source")).AsTask());

        Assert.Equal("SNR-ACQ-006", exception.Code);
        Assert.Single(corpus.Captured);
        Assert.Equal("application/json", corpus.Captured[0].ContentType);
    }

    [Theory]
    [InlineData("http://example.test/product")]
    [InlineData("ftp://example.test/product")]
    public async Task AcquireAsync_rejects_an_insecure_transport(string url)
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("The transport must be rejected before sending."));
        var acquirer = new HttpContentAcquirer(new HttpClient(handler), new TestFixtureCorpus());

        var exception = await Assert.ThrowsAsync<AcquisitionException>(() => acquirer.AcquireAsync(new AcquisitionRequest(new Uri(url), "source")).AsTask());

        Assert.Equal("SNR-ACQ-009", exception.Code);
        Assert.Equal(0, handler.SendCount);
    }

    [Fact]
    public async Task AcquireAsync_rejects_a_body_that_exceeds_the_response_ceiling()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("0123456789", Encoding.UTF8, "text/html"),
        });
        var acquirer = new HttpContentAcquirer(new HttpClient(handler), new TestFixtureCorpus(), new AcquisitionOptions(MaximumResponseBytes: 5));

        var exception = await Assert.ThrowsAsync<AcquisitionException>(() => acquirer.AcquireAsync(new AcquisitionRequest(new Uri("https://example.test/product"), "source")).AsTask());

        Assert.Equal("SNR-ACQ-007", exception.Code);
    }

    private static HttpResponseMessage CreateResponse(byte[] body, string mediaType, string? charset = null)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType) { CharSet = charset };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int SendCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SendCount++;
            return Task.FromResult(responder(request));
        }
    }

    private sealed class TestFixtureCorpus : IFixtureCorpus
    {
        private readonly Dictionary<string, FixtureContent> _contents = [];
        private readonly List<FixtureRecord> _records = [];
        public List<CaptureRequest> Captured { get; } = [];

        public void Add(string sourceId, Uri url, string pageRole, string content)
        {
            var id = Guid.NewGuid().ToString("N");
            var record = new FixtureRecord(id, sourceId, url.AbsoluteUri, DateTimeOffset.UtcNow, AcquisitionTier.Html, "text/html", "unused", content.Length, "hash", "hash", [], [], pageRole, null, RetentionTier.Full, null);
            _records.Add(record);
            _contents.Add(id, new FixtureContent(Encoding.UTF8.GetBytes(content), "text/html", record));
        }

        public async ValueTask<FixtureRecord> CaptureAsync(CaptureRequest request, CancellationToken ct = default)
        {
            Captured.Add(request);
            using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
            var bytes = Encoding.UTF8.GetBytes(await reader.ReadToEndAsync(ct));
            var id = Guid.NewGuid().ToString("N");
            var record = new FixtureRecord(id, request.SourceId, request.Url, DateTimeOffset.UtcNow, request.Tier, request.ContentType, "unused", bytes.Length, "hash", "hash", [], [], request.PageRole, request.Notes, request.RetentionTier, request.PinnedIssue);
            _records.Add(record);
            _contents.Add(id, new FixtureContent(bytes, request.ContentType, record));
            return record;
        }

        public ValueTask<FixtureContent?> GetContentAsync(string fixtureId, CancellationToken ct = default) => ValueTask.FromResult(_contents.GetValueOrDefault(fixtureId));
        public ValueTask<IReadOnlyList<FixtureRecord>> QueryAsync(FixtureQuery query, CancellationToken ct = default) => ValueTask.FromResult<IReadOnlyList<FixtureRecord>>(_records.Where(record => (query.SourceId is null || record.SourceId == query.SourceId) && (query.Url is null || record.Url == query.Url) && (query.PageRole is null || record.PageRole == query.PageRole)).ToList());
        public ValueTask<FixtureSlice> SliceAsync(string fixtureId, FixtureSliceRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<PruneReport> PruneAsync(IReadOnlyCollection<string> protectedFixtureIds, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
