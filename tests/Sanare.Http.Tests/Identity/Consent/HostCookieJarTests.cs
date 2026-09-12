using System.Net;
using Sanare.Http.Identity.Consent;

namespace Sanare.Http.Tests.Identity.Consent;

public sealed class HostCookieJarTests
{
    [Fact]
    public void Get_returns_a_cookie_that_was_set_for_the_same_host()
    {
        var jar = new HostCookieJar();
        jar.Set("example.test", new Cookie("consent", "1", "/", "example.test"));

        var cookies = jar.Get("example.test");

        Assert.Single(cookies);
        Assert.Equal("consent", cookies[0].Name);
        Assert.Equal("1", cookies[0].Value);
    }

    [Fact]
    public void Get_returns_no_cookies_for_a_different_host()
    {
        var jar = new HostCookieJar();
        jar.Set("example.test", new Cookie("consent", "1", "/", "example.test"));

        Assert.Empty(jar.Get("other.test"));
    }

    [Fact]
    public void Get_returns_no_cookies_for_a_host_that_was_never_set()
    {
        var jar = new HostCookieJar();
        Assert.Empty(jar.Get("never-set.test"));
    }

    [Fact]
    public void Set_replaces_a_cookie_with_the_same_name_for_the_same_host()
    {
        var jar = new HostCookieJar();
        jar.Set("example.test", new Cookie("consent", "1", "/", "example.test"));
        jar.Set("example.test", new Cookie("consent", "2", "/", "example.test"));

        var cookies = jar.Get("example.test");

        Assert.Single(cookies);
        Assert.Equal("2", cookies[0].Value);
    }

    [Fact]
    public void Get_excludes_and_evicts_a_cookie_that_has_expired_per_the_injected_clock()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        var jar = new HostCookieJar(clock);
        jar.Set("example.test", new Cookie("consent", "1", "/", "example.test") { Expires = DateTime.Parse("2024-01-01T00:00:01Z").ToUniversalTime() });

        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.Empty(jar.Get("example.test"));
    }

    [Fact]
    public void Get_includes_a_cookie_that_has_not_yet_expired_per_the_injected_clock()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        var jar = new HostCookieJar(clock);
        jar.Set("example.test", new Cookie("consent", "1", "/", "example.test") { Expires = DateTime.Parse("2024-01-01T00:10:00Z").ToUniversalTime() });

        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.Single(jar.Get("example.test"));
    }

    [Fact]
    public void Clear_removes_every_cookie_held_for_the_given_host()
    {
        var jar = new HostCookieJar();
        jar.Set("example.test", new Cookie("a", "1", "/", "example.test"));
        jar.Set("example.test", new Cookie("b", "2", "/", "example.test"));

        jar.Clear("example.test");

        Assert.Empty(jar.Get("example.test"));
    }

    [Fact]
    public void Host_scoping_is_case_insensitive()
    {
        var jar = new HostCookieJar();
        jar.Set("Example.Test", new Cookie("consent", "1", "/", "example.test"));

        Assert.Single(jar.Get("example.test"));
    }

    [Fact]
    public void Set_bounds_the_number_of_cookies_retained_per_host()
    {
        var jar = new HostCookieJar(maxCookiesPerHost: 2);
        jar.Set("example.test", new Cookie("a", "1", "/", "example.test"));
        jar.Set("example.test", new Cookie("b", "2", "/", "example.test"));
        jar.Set("example.test", new Cookie("c", "3", "/", "example.test"));

        Assert.True(jar.Get("example.test").Count <= 2);
    }

    private sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
