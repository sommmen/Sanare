using System.Text.Json;

namespace Sanare.Core.Fixtures;

public sealed class FixtureManifestStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    private readonly string _path;

    public FixtureManifestStore(string stateRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateRoot);
        _path = System.IO.Path.Combine(stateRoot, "fixtures", "manifest.json");
    }

    public string Path => _path;

    public async ValueTask<FixtureManifest> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path)) { return new FixtureManifest(1, []); }
        try
        {
            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<FixtureManifest>(stream, SerializerOptions, ct).ConfigureAwait(false)
                ?? throw new FixtureCorpusException("SNR-FIX-002", $"Fixture manifest '{_path}' is empty.");
        }
        catch (JsonException exception)
        {
            throw new FixtureCorpusException("SNR-FIX-002", $"Fixture manifest '{_path}' is invalid: {exception.Message}");
        }
    }

    public async ValueTask SaveAsync(FixtureManifest manifest, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var directory = System.IO.Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, manifest, SerializerOptions, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) { File.Delete(temporaryPath); }
        }
    }
}
