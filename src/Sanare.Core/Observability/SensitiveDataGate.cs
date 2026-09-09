using Microsoft.Extensions.Logging;

namespace Sanare.Core.Observability;

/// <summary>
/// Gates <c>EnableSensitiveData</c> behind an encrypted state-root volume, per SNR-OBS-003: turning this on
/// records raw prompts/completions, which for this library means raw page HTML.
/// </summary>
public sealed class SensitiveDataGate
{
    private readonly ILogger<SensitiveDataGate>? _logger;

    public SensitiveDataGate(ILogger<SensitiveDataGate>? logger = null) => _logger = logger;

    /// <summary>
    /// Returns whether sensitive data capture may be enabled. When <paramref name="requested"/> is
    /// <see langword="true"/> but <paramref name="stateRootIsEncrypted"/> is <see langword="false"/>, the
    /// request is refused (this method returns <see langword="false"/>) and the refusal is logged.
    /// </summary>
    public bool Resolve(bool requested, bool stateRootIsEncrypted)
    {
        if (!requested)
        {
            return false;
        }

        if (stateRootIsEncrypted)
        {
            return true;
        }

        _logger?.LogError(
            "SNR-OBS-003: EnableSensitiveData was requested but the state root is not on an encrypted " +
            "volume. Refusing to enable it; continuing with sensitive-data capture off.");
        return false;
    }
}
