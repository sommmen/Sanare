using Sanare.Abstractions.Attributes;

namespace Sanare.Samples.Lenovo;

public sealed class TabletListing
{
    [ScrapeField(Required = true, Description = "The marketing product name shown on the product card")]
    public string Name { get; set; } = string.Empty;

    public Uri ProductUrl { get; set; } = null!;

    [ScrapeCulture("nl-NL")]
    public decimal? Price { get; set; }

    public string? Currency { get; set; }

    public string? Availability { get; set; }

    public Uri? ImageUrl { get; set; }

    public string? Badge { get; set; }

    public string? ShortDescription { get; set; }
}

public sealed class TabletProduct
{
    [ScrapeField(Required = true)]
    public string Name { get; set; } = string.Empty;

    public string? PartNumber { get; set; }

    [ScrapeCulture("nl-NL")]
    public decimal? Price { get; set; }

    public string? Currency { get; set; }

    public string? Availability { get; set; }

    public IReadOnlyList<Uri> Images { get; set; } = [];

    [ScrapeCollection(ItemName = "specification")]
    public IReadOnlyList<ProductSpecification> Specifications { get; set; } = [];
}

public sealed class ProductSpecification
{
    public string? Group { get; set; }

    [ScrapeField(Required = true)]
    public string Name { get; set; } = string.Empty;

    [ScrapeField(Required = true)]
    public string Value { get; set; } = string.Empty;
}

/// <summary>Per-plan outcome reported by the <c>validate</c> command.</summary>
public sealed class PlanValidationReport
{
    public string PlanSourceId { get; set; } = string.Empty;

    public bool IsValid { get; set; }

    public IReadOnlyList<string> Defects { get; set; } = [];
}
