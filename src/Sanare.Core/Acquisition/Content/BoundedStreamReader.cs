namespace Sanare.Core.Acquisition.Content;

/// <summary>
/// Streams a response body into memory while enforcing a byte ceiling, aborting as soon as the
/// ceiling is crossed rather than after the whole body has been buffered
/// (docs/features/acquisition-pipeline.md, "Key Behaviors" &gt; "Safety limits", AC-ACQ-016).
/// </summary>
public static class BoundedStreamReader
{
    private const int BufferSize = 81920;

    /// <summary>
    /// Reads <paramref name="stream"/> to its end, throwing <see cref="AcquisitionException"/> with
    /// <paramref name="overflowCode"/> the moment the accumulated length would exceed
    /// <paramref name="maximumBytes"/>.
    /// </summary>
    /// <param name="stream">The response body stream.</param>
    /// <param name="maximumBytes">The inclusive byte ceiling.</param>
    /// <param name="overflowCode">The error code to raise on overflow: <c>SNR-ACQ-007</c> for a target body, <c>SNR-ACQ-010</c> for a discovery document.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async ValueTask<byte[]> ReadAsync(Stream stream, long maximumBytes, string overflowCode, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(overflowCode);

        using var output = new MemoryStream();
        var buffer = new byte[BufferSize];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
        {
            if (output.Length + read > maximumBytes)
            {
                throw new AcquisitionException(overflowCode, $"The response body exceeds the configured ceiling of {maximumBytes} bytes.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }

        return output.ToArray();
    }
}
