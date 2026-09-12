using System.Net;
using Sanare.Http.Identity;

namespace Sanare.Http.Tests.Identity;

public sealed class BrowsingIdentityExtensionsTests
{
    [Fact]
    public void ToRequestIdentity_preserves_the_profile_headers_and_cookies_in_order()
    {
        var headers = new List<KeyValuePair<string, string>>
        {
            new("Accept", "text/html"),
            new("User-Agent", "SanareTest/1.0"),
        };
        var cookies = new List<Cookie>
        {
            new("a", "1"),
            new("b", "2"),
        };
        var identity = new BrowsingIdentity("test-profile", headers, cookies);

        var requestIdentity = identity.ToRequestIdentity();

        Assert.Equal("test-profile", requestIdentity.ProfileId);
        Assert.Equal(headers, requestIdentity.Headers);
        Assert.Equal(cookies, requestIdentity.Cookies);
    }

    [Fact]
    public void ToRequestIdentity_rejects_a_null_identity()
    {
        BrowsingIdentity? identity = null;

        Assert.Throws<ArgumentNullException>(() => identity!.ToRequestIdentity());
    }
}
