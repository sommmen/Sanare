using System.Text.Json.Serialization;

namespace Sanare.Samples.Lenovo;

[JsonSerializable(typeof(TabletListing))]
[JsonSerializable(typeof(TabletListing[]))]
[JsonSerializable(typeof(TabletProduct))]
[JsonSerializable(typeof(ProductSpecification))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class LenovoJsonContext : JsonSerializerContext;
