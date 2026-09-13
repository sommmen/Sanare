using System.Text.RegularExpressions;
using PublicApiGenerator;
using Sanare.Http.Identity;

namespace Sanare.Http.Tests.ApprovedApi;

public sealed class ApiSurfaceTests
{
    private const string UpdateApprovedApiEnvironmentVariable = "SANARE_UPDATE_APPROVED_API";

    [Fact]
    public void Public_api_matches_the_approved_surface()
    {
        var actual = typeof(BrowsingIdentity).Assembly.GeneratePublicApi();
        var approvedPath = Path.Combine(AppContext.BaseDirectory, "ApprovedApi", "Sanare.Http.approved.txt");

        if (string.Equals(Environment.GetEnvironmentVariable(UpdateApprovedApiEnvironmentVariable), "1", StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(approvedPath)!);
            File.WriteAllText(approvedPath, actual);
        }

        Assert.True(File.Exists(approvedPath), $"Missing approved public API file '{approvedPath}'. Set {UpdateApprovedApiEnvironmentVariable}=1 and rerun this test to generate it.");
        Assert.Equal(NormalizeLineEndings(File.ReadAllText(approvedPath)), NormalizeLineEndings(actual));
    }

    [Fact]
    public void Public_api_does_not_expose_prohibited_automation_or_credential_capabilities()
    {
        var api = typeof(BrowsingIdentity).Assembly.GeneratePublicApi().ToLowerInvariant();
        var prohibitedFragments = new[] { "fingerprint", "captcha", "password", "credential" };

        foreach (var fragment in prohibitedFragments)
        {
            Assert.DoesNotContain(fragment, api);
        }
    }

    [Fact]
    public void Assembly_exposes_no_captcha_solver_type()
    {
        // "solver" must not be matched as a bare substring: it also occurs inside "Resolver", which is
        // ordinary naming for types that look something up (DiscoveryDocumentResolver). The negative
        // lookbehind keeps CaptchaSolver, ChallengeSolver, and friends caught while letting Resolver pass.
        var solverName = new Regex("(?<!re)solver", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        Assert.DoesNotContain(typeof(BrowsingIdentity).Assembly.GetExportedTypes(), type =>
            solverName.IsMatch(type.Name));
    }

    private static string NormalizeLineEndings(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
}
