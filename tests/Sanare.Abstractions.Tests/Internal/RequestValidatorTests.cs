using Sanare.Abstractions.Internal;

namespace Sanare.Abstractions.Tests.Internal;

public sealed class RequestValidatorTests
{
    private static readonly Uri ValidUrl = new("https://www.lenovo.example/tablets/yoga-tab");

    [Fact]
    public void Validate_accepts_a_well_formed_request_and_derives_source_id()
    {
        var request = new ScrapeRequest { Url = ValidUrl };

        var result = RequestValidator.Validate(request);

        Assert.Null(result.FailureStatus);
        Assert.NotNull(result.NormalizedRequest);
        Assert.Equal("www-lenovo-example/tablets", result.NormalizedRequest!.SourceId);
    }

    [Fact]
    public void Validate_derives_a_slugged_source_id_from_encoded_path_segments()
    {
        var request = new ScrapeRequest { Url = new Uri("https://www.example.com/Foo%20Bar/item") };

        var result = RequestValidator.Validate(request);

        Assert.Null(result.FailureStatus);
        Assert.Equal("www-example-com/foo-bar", result.NormalizedRequest!.SourceId);
    }

    [Fact]
    public void Validate_preserves_an_explicit_source_id()
    {
        var request = new ScrapeRequest { Url = ValidUrl, SourceId = "lenovo/tablets" };

        var result = RequestValidator.Validate(request);

        Assert.Null(result.FailureStatus);
        Assert.Equal("lenovo/tablets", result.NormalizedRequest!.SourceId);
    }

    [Fact]
    public void Validate_rejects_a_relative_url()
    {
        var request = new ScrapeRequest { Url = new Uri("/tablets/yoga-tab", UriKind.Relative) };

        var result = RequestValidator.Validate(request);

        Assert.Equal(ScrapeStatus.InvalidRequest, result.FailureStatus);
        Assert.Null(result.NormalizedRequest);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "SNR-API-001");
    }

    [Fact]
    public void Validate_rejects_a_malformed_source_id()
    {
        var request = new ScrapeRequest { Url = ValidUrl, SourceId = "Not Valid!" };

        var result = RequestValidator.Validate(request);

        Assert.Equal(ScrapeStatus.InvalidRequest, result.FailureStatus);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "SNR-API-002");
    }

    [Fact]
    public void Validate_clamps_max_items_with_a_warning()
    {
        var request = new ScrapeRequest { Url = ValidUrl, MaxItems = -5 };

        var result = RequestValidator.Validate(request);

        Assert.Null(result.FailureStatus);
        Assert.Equal(1, result.NormalizedRequest!.MaxItems);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "SNR-API-003");
    }

    [Fact]
    public void Validate_rejects_freshness_outside_the_supported_range()
    {
        var request = new ScrapeRequest { Url = ValidUrl, Freshness = TimeSpan.FromDays(31) };

        var result = RequestValidator.Validate(request);

        Assert.Equal(ScrapeStatus.InvalidRequest, result.FailureStatus);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "SNR-API-004");
    }

    [Fact]
    public void Validate_rejects_an_unresolvable_culture()
    {
        var request = new ScrapeRequest { Url = ValidUrl, Culture = "this-is-not-a-culture-name" };

        var result = RequestValidator.Validate(request);

        Assert.Equal(ScrapeStatus.InvalidRequest, result.FailureStatus);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "SNR-API-005");
    }
}
