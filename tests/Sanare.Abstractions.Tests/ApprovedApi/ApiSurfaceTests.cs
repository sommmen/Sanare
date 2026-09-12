using System.Xml.Linq;
using PublicApiGenerator;
using Sanare.Abstractions;

namespace Sanare.Abstractions.Tests.ApprovedApi;

/// <summary>
/// Freezes the public surface of <c>Sanare.Abstractions</c> and enforces the "BCL only"
/// dependency constraint automatically, closing docs/features/scrape-api-contracts.md AC-014
/// and AC-015 (previously "enforced by review only").
/// </summary>
public sealed class ApiSurfaceTests
{
    private const string UpdateApprovedApiEnvironmentVariable = "SANARE_UPDATE_APPROVED_API";

    [Fact]
    public void Public_api_matches_the_approved_surface()
    {
        var actual = typeof(IScrapeRunner).Assembly.GeneratePublicApi();
        var approvedPath = Path.Combine(AppContext.BaseDirectory, "ApprovedApi", "Sanare.Abstractions.approved.txt");

        if (string.Equals(Environment.GetEnvironmentVariable(UpdateApprovedApiEnvironmentVariable), "1", StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(approvedPath)!);
            File.WriteAllText(approvedPath, actual);
        }

        Assert.True(File.Exists(approvedPath), $"Missing approved public API file '{approvedPath}'. Set {UpdateApprovedApiEnvironmentVariable}=1 and rerun this test to generate it.");
        Assert.Equal(NormalizeLineEndings(File.ReadAllText(approvedPath)), NormalizeLineEndings(actual));
    }

    [Fact]
    public void Project_file_declares_no_third_party_package_references()
    {
        var projectPath = FindProjectFile();
        var document = XDocument.Load(projectPath);

        var packageReferences = document.Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .Where(include => !string.IsNullOrEmpty(include))
            .ToList();

        Assert.True(
            packageReferences.Count == 0,
            $"Sanare.Abstractions.csproj must have zero PackageReference items (BCL only); found: {string.Join(", ", packageReferences)}");
    }

    private static string FindProjectFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Sanare.Abstractions", "Sanare.Abstractions.csproj");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate Sanare.Abstractions.csproj by walking up from the test output directory.");
    }

    private static string NormalizeLineEndings(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
}
