using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace Sanare.Browser.Tests;

/// <summary>Serves the dependency-free browser fixtures on an isolated loopback port.</summary>
public sealed class BrowserTestSiteFixture : IAsyncLifetime
{
    private WebApplication? _application;
    private long _bytesServed;

    /// <summary>The loopback base URI assigned to this fixture.</summary>
    public Uri BaseUri { get; private set; } = null!;

    /// <summary>Total response bytes served since the last reset.</summary>
    public long BytesServed => Interlocked.Read(ref _bytesServed);

    /// <summary>Builds an absolute URI to a static test-site resource.</summary>
    public Uri UriFor(string relativePath) => new(BaseUri, relativePath);

    /// <summary>Clears the response-byte counter used by resource-blocking tests.</summary>
    public void ResetByteCount() => Interlocked.Exchange(ref _bytesServed, 0);

    public async Task InitializeAsync()
    {
        var sitePath = Path.Combine(AppContext.BaseDirectory, "TestSite");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = sitePath });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var application = builder.Build();
        application.Use(async (context, next) =>
        {
            await next(context).ConfigureAwait(false);
            Interlocked.Add(ref _bytesServed, context.Response.ContentLength ?? 0);
        });
        application.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(sitePath),
        });
        await application.StartAsync().ConfigureAwait(false);

        var addresses = application.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()?.Addresses
            ?? throw new InvalidOperationException("Kestrel did not expose a bound address.");
        BaseUri = new Uri(addresses.Single());
        _application = application;
    }

    public async Task DisposeAsync()
    {
        if (_application is not null)
        {
            await _application.StopAsync().ConfigureAwait(false);
            await _application.DisposeAsync().ConfigureAwait(false);
        }
    }
}