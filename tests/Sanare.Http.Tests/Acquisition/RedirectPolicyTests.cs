using Sanare.Core.Acquisition;
using Sanare.Http.Redirects;

namespace Sanare.Http.Tests.Acquisition;

public sealed class RedirectPolicyTests
{
    private static readonly Uri From = new("https://example.test/a");

    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(303)]
    [InlineData(307)]
    [InlineData(308)]
    public void Redirect_statuses_are_recognised(int statusCode) =>
        Assert.True(RedirectPolicy.IsRedirect(statusCode));

    [Theory]
    [InlineData(200)]
    [InlineData(304)]
    [InlineData(404)]
    public void Non_redirect_statuses_are_not(int statusCode) =>
        Assert.False(RedirectPolicy.IsRedirect(statusCode));

    [Fact]
    public void A_relative_location_resolves_against_the_current_url()
    {
        var hop = new RedirectPolicy().ShouldFollow(From, "/b", 302, hopsSoFar: 0);

        Assert.Equal(new Uri("https://example.test/b"), hop.To);
        Assert.False(hop.CrossHost);
    }

    [Fact]
    public void A_cross_host_hop_is_flagged()
    {
        // The flag is what makes the caller re-check robots against the new host rather than carrying the
        // origin's permission across the boundary.
        var hop = new RedirectPolicy().ShouldFollow(From, "https://other.test/b", 302, hopsSoFar: 0);

        Assert.True(hop.CrossHost);
    }

    [Fact]
    public void The_hop_limit_is_enforced()
    {
        var policy = new RedirectPolicy(maximumHops: 3);

        policy.ShouldFollow(From, "/b", 302, hopsSoFar: 2);

        var exception = Assert.Throws<AcquisitionException>(() => policy.ShouldFollow(From, "/b", 302, hopsSoFar: 3));
        Assert.Equal("SNR-ACQ-008", exception.Code);
    }

    [Fact]
    public void A_downgrade_to_http_is_refused()
    {
        // Following https -> http would hand the whole request, headers included, to the network in clear.
        var exception = Assert.Throws<AcquisitionException>(
            () => new RedirectPolicy().ShouldFollow(From, "http://example.test/b", 302, hopsSoFar: 0));

        Assert.Equal("SNR-ACQ-013", exception.Code);
    }

    [Fact]
    public void A_downgrade_is_allowed_only_when_explicitly_opted_into()
    {
        var hop = new RedirectPolicy(allowInsecureTransport: true)
            .ShouldFollow(From, "http://example.test/b", 302, hopsSoFar: 0);

        Assert.Equal("http", hop.To.Scheme);
    }

    [Fact]
    public void A_missing_location_header_is_a_transport_failure()
    {
        var exception = Assert.Throws<AcquisitionException>(
            () => new RedirectPolicy().ShouldFollow(From, location: null, 302, hopsSoFar: 0));

        Assert.Equal("SNR-ACQ-001", exception.Code);
    }

    [Fact]
    public void A_non_http_scheme_is_refused()
    {
        Assert.ThrowsAny<AcquisitionException>(
            () => new RedirectPolicy().ShouldFollow(From, "ftp://example.test/b", 302, hopsSoFar: 0));
    }
}
