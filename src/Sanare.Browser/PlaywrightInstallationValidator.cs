using Sanare.Core.Acquisition;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Sanare.Browser.Tests")]

namespace Sanare.Browser;

/// <summary>
/// Validates that Playwright's Chromium browser is installed before the browser tier is allowed to run
/// (docs/features/browser-tier.md, "Task order" T2; AC-BRW-011 "startup validation"). Runs from
/// <c>AcquisitionPipelineFactory</c> only when <c>BrowserOptions.Enabled</c> is <see langword="true"/>, so
/// an HTTP-only host never needs Playwright installed and never pays this check.
/// </summary>
public interface IPlaywrightInstallationValidator
{
    /// <summary>
    /// Throws <see cref="AcquisitionException"/> with code <c>SNR-BRW-005</c> when the Chromium
    /// executable Playwright expects is not present on disk.
    /// </summary>
    void Validate();
}

/// <inheritdoc cref="IPlaywrightInstallationValidator" />
public sealed class PlaywrightInstallationValidator : IPlaywrightInstallationValidator
{
    private readonly Func<string> _resolveChromiumExecutablePath;

    /// <summary>Creates a validator that resolves the executable path via the installed Playwright driver.</summary>
    public PlaywrightInstallationValidator()
        : this(ResolveChromiumExecutablePath)
    {
    }

    /// <summary>Creates a validator using a supplied path resolver, for testing without a real Playwright install.</summary>
    /// <param name="resolveChromiumExecutablePath">Returns the path Playwright expects the Chromium executable at.</param>
    internal PlaywrightInstallationValidator(Func<string> resolveChromiumExecutablePath)
    {
        ArgumentNullException.ThrowIfNull(resolveChromiumExecutablePath);
        _resolveChromiumExecutablePath = resolveChromiumExecutablePath;
    }

    /// <inheritdoc />
    public void Validate()
    {
        var executablePath = _resolveChromiumExecutablePath();

        if (!File.Exists(executablePath))
        {
            throw new AcquisitionException(
                "SNR-BRW-005",
                $"Playwright's Chromium browser is not installed (expected at '{executablePath}'). " +
                "Run `pwsh bin/Debug/net10.0/playwright.ps1 install chromium` to install it.");
        }
    }

    private static string ResolveChromiumExecutablePath()
    {
        using var playwright = Microsoft.Playwright.Playwright.CreateAsync().GetAwaiter().GetResult();
        return playwright.Chromium.ExecutablePath;
    }
}
