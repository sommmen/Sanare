using Sanare.Core.Acquisition;

namespace Sanare.Browser.Tests;

/// <summary>
/// Covers <see cref="PlaywrightInstallationValidator"/> (docs/features/browser-tier.md, "Task order" T2;
/// AC-BRW-011). Uses the internal path-resolver constructor so these run without a real Playwright/Chromium
/// install and stay untagged (no <c>Category=Browser</c> trait), unlike the later integration tests that
/// actually launch a browser.
/// </summary>
public sealed class PlaywrightInstallationValidatorTests
{
    [Fact]
    public void Validate_does_nothing_when_the_chromium_executable_exists()
    {
        var existingPath = Path.Combine(AppContext.BaseDirectory, "Sanare.Browser.Tests.dll");
        var validator = new PlaywrightInstallationValidator(() => existingPath);

        var exception = Record.Exception(validator.Validate);

        Assert.Null(exception);
    }

    [Fact]
    public void Validate_throws_SNR_BRW_005_when_the_chromium_executable_is_missing()
    {
        var missingPath = Path.Combine(AppContext.BaseDirectory, "definitely-not-a-real-chromium-binary.exe");
        var validator = new PlaywrightInstallationValidator(() => missingPath);

        var exception = Assert.Throws<AcquisitionException>(validator.Validate);

        Assert.Equal("SNR-BRW-005", exception.Code);
        Assert.Contains("playwright.ps1 install chromium", exception.Message, StringComparison.Ordinal);
    }
}
