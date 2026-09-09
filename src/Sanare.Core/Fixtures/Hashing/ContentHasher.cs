using System.Security.Cryptography;
using System.Text;

namespace Sanare.Core.Fixtures.Hashing;

public sealed class ContentHasher
{
    private readonly HtmlNormalizer _htmlNormalizer = new();
    private readonly JsonNormalizer _jsonNormalizer;

    public ContentHasher(IReadOnlyCollection<string>? volatileJsonKeys = null) => _jsonNormalizer = new JsonNormalizer(volatileJsonKeys);

    public string ContentHash(ReadOnlySpan<byte> bytes) => Hash(bytes);

    public string NormalisedHash(ReadOnlySpan<byte> bytes, string contentType)
    {
        var text = Encoding.UTF8.GetString(bytes);
        var normalised = contentType.Contains("html", StringComparison.OrdinalIgnoreCase)
            ? _htmlNormalizer.Normalize(text)
            : contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
                ? _jsonNormalizer.Normalize(text)
                : text;
        return Hash(Encoding.UTF8.GetBytes(normalised));
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
