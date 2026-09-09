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
            var manifest = await JsonSerializer.DeserializeAsync<FixtureManifest>(stream, SerializerOptions, ct).ConfigureAwait(false)
                ?? throw new FixtureCorpusException("SNR-FIX-002", $"Fixture manifest '{_path}' is empty.");
            if (manifest.Fixtures is null)
            {
                throw new FixtureCorpusException("SNR-FIX-002", $"Fixture manifest '{_path}' is missing its fixtures list.");
            }

            foreach (var fixture in manifest.Fixtures)
            {
                ValidateFixture(fixture);
            }

            return manifest;
        }
        catch (JsonException exception)
        {
            throw new FixtureCorpusException("SNR-FIX-002", $"Fixture manifest '{_path}' is invalid: {exception.Message}");
        }
    }

    /// <summary>Fails loudly with <c>SNR-FIX-002</c> when a syntactically-valid manifest entry is
    /// semantically incomplete (null required string/list members), rather than letting it crash later
    /// with an unrelated <see cref="NullReferenceException"/> the first time that member is used.</summary>
    private void ValidateFixture(FixtureRecord fixture)
    {
        if (fixture.Id is null || fixture.SourceId is null || fixture.Url is null || fixture.ContentType is null ||
            fixture.File is null || fixture.ContentHash is null || fixture.NormalisedHash is null ||
            fixture.Redactions is null || fixture.ReferencedByTags is null)
        {
            throw new FixtureCorpusException("SNR-FIX-002", $"Fixture manifest '{_path}' contains an entry with missing required fields.");
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
