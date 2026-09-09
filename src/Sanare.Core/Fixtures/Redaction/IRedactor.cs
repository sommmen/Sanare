namespace Sanare.Core.Fixtures.Redaction;

public interface IRedactor
{
    RedactionResult Redact(ReadOnlySpan<byte> content, IReadOnlyDictionary<string, string>? headers = null);
}

public sealed record RedactionResult(byte[] Content, IReadOnlyList<string> Rules, IReadOnlyDictionary<string, string> Headers);
