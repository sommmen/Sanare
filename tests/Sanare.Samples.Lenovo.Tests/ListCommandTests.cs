using System.Text.Json;
using Sanare.Core.Plans;
using Sanare.Core.Schema;
using Sanare.Samples.Lenovo;

namespace Sanare.Samples.Lenovo.Tests;

public sealed class ListCommandTests
{
    private static readonly Lock ConsoleLock = new();

    [Fact]
    public void Lister_plan_is_canonical_and_valid_for_the_tablet_listing_schema()
    {
        var path = Path.Combine(FindRepositoryRoot(), "samples", "Sanare.Samples.Lenovo.State", "scripts", "plans", "lenovo-com", "tablet-lister.json");
        var json = File.ReadAllText(path);
        var serializer = new PlanSerializer();
        var plan = serializer.Read(json);

        var validation = new PlanValidator().Validate(plan, new SchemaDeriver().Derive<TabletListing>("nl-NL"));
        Assert.True(validation.IsValid, string.Join("; ", validation.Defects.Select(static defect => defect.Message)));
    }

    [Fact]
    public void List_offline_writes_unique_absolute_product_urls_as_json()
    {
        lock (ConsoleLock)
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var originalOut = Console.Out;
            var originalError = Console.Error;
            try
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);
                Assert.Equal(0, Program.Main(["list", "--offline"]));
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }

            using var document = JsonDocument.Parse(stdout.ToString());
            var listings = document.RootElement.EnumerateArray().ToArray();
            Assert.NotEmpty(listings);
            var urls = listings.Select(static listing => listing.GetProperty("productUrl").GetString()).ToArray();
            Assert.Equal(urls.Length, urls.Distinct(StringComparer.Ordinal).Count());
            Assert.All(urls, static url => Assert.True(Uri.TryCreate(url, UriKind.Absolute, out _)));
            Assert.Equal(string.Empty, stderr.ToString());
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Sanare.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
