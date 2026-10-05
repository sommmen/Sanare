using System.Text.Json;
using Sanare.Core.Fixtures;
using Sanare.Core.Plans;
using Sanare.Core.Runtime;
using Sanare.Core.Schema;
using Sanare.Core.Schema.Coercion;
using Sanare.Core.Schema.Materialization;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Sanare.Samples.Lenovo.Tests")]

namespace Sanare.Samples.Lenovo;

public static class Program
{
    private const string DetailSourceId = "lenovo-com/tablet-detail";
    private const string DetailUrl = "https://www.lenovo.com/nl/nl/p/tablets/android-tablets/lenovo-tab-series/lenovo-yoga-tab-gen-2/len103y0003";
    private const string ListerSourceId = "lenovo-com/tablet-lister";
    private const string ListerUrl = "https://www.lenovo.com/nl/nl/tablets/";

    public static int Main(string[] args)
    {
        if (args is ["detail", "--offline"])
        {
            return RunDetail();
        }

        if (args is ["list", "--offline"])
        {
            return RunList();
        }

        if (args is ["validate"])
        {
            return RunValidate();
        }

        Console.Error.WriteLine("Usage: Sanare.Samples.Lenovo <detail|list|validate> [--offline]");
        return 0;
    }

    private static int RunDetail()
    {
        try
        {
            var root = FindRepositoryRoot();
            var state = Path.Combine(root, "samples", "Sanare.Samples.Lenovo.State");
            var planPath = Path.Combine(state, "scripts", "plans", "lenovo-com", "tablet-detail.json");
            var plan = new PlanSerializer().Read(File.ReadAllText(planPath));
            var schema = new SchemaDeriver().Derive<TabletProduct>("nl-NL");
            var scalarSchema = schema with
            {
                Fields = schema.Fields.Where(field => !field.JsonPointer.StartsWith("/Specifications/", StringComparison.Ordinal)).ToArray(),
            };
            var fixtures = new FileFixtureContentProvider(
                Path.Combine(state, "fixtures"),
                Path.Combine(state, "fixtures", "manifest.json"));
            if (!fixtures.TryGet(DetailSourceId, new Uri(DetailUrl), out var content))
            {
                return Fail("Offline fixture was not found.");
            }

            var executor = new PlanExecutor(new TypeCoercer());
            var productOutcome = executor.Execute(plan, content, scalarSchema);
            var specificationOutcomes = executor.ExecuteMany(plan, content, schema);
            if (!productOutcome.RequiredFieldsPresent || specificationOutcomes.Any(static outcome => !outcome.RequiredFieldsPresent))
            {
                var diagnostics = productOutcome.Diagnostics
                    .Concat(specificationOutcomes.SelectMany(static outcome => outcome.Diagnostics))
                    .Where(static diagnostic => diagnostic.Severity == Sanare.Abstractions.Diagnostics.DiagnosticSeverity.Error)
                    .Select(static diagnostic => diagnostic.Message);
                return Fail($"Extraction failed required-field validation: {string.Join("; ", diagnostics)}");
            }

            var materializer = new DocumentMaterializer();
            var product = materializer.Materialize<TabletProduct>(productOutcome.Values);
            product.Specifications = specificationOutcomes
                .Select(outcome => materializer.Materialize<ProductSpecification>(outcome.Values))
                .ToArray();

            var validation = new SchemaValidator().Validate(scalarSchema, productOutcome.Values);
            if (!validation.IsValid)
            {
                return Fail("Extracted product did not validate against its schema.");
            }

            Console.Out.WriteLine(JsonSerializer.Serialize(product, LenovoJsonContext.Default.TabletProduct));
            return 0;
        }
        catch (Exception exception)
        {
            return Fail(exception.Message);
        }
    }

    private static int RunList()
    {
        try
        {
            var root = FindRepositoryRoot();
            var state = Path.Combine(root, "samples", "Sanare.Samples.Lenovo.State");
            var planPath = Path.Combine(state, "scripts", "plans", "lenovo-com", "tablet-lister.json");
            var plan = new PlanSerializer().Read(File.ReadAllText(planPath));
            var schema = new SchemaDeriver().Derive<TabletListing>("nl-NL");
            var fixtures = new FileFixtureContentProvider(
                Path.Combine(state, "fixtures"),
                Path.Combine(state, "fixtures", "manifest.json"));
            if (!fixtures.TryGet(ListerSourceId, new Uri(ListerUrl), out var content))
            {
                return Fail("Offline fixture was not found.");
            }

            var outcomes = new PlanExecutor(new TypeCoercer()).ExecuteMany(plan, content, schema);
            if (outcomes.Any(static outcome => !outcome.RequiredFieldsPresent))
            {
                return Fail("Extraction failed required-field validation.");
            }

            var materializer = new DocumentMaterializer();
            var listings = outcomes
                .Select(outcome => materializer.Materialize<TabletListing>(outcome.Values))
                .DistinctBy(static listing => listing.ProductUrl)
                .ToArray();
            if (listings.Length == 0)
            {
                return Fail("No tablet listings were extracted.");
            }

            Console.Out.WriteLine(JsonSerializer.Serialize(listings, LenovoJsonContext.Default.TabletListingArray));
            return 0;
        }
        catch (Exception exception)
        {
            return Fail(exception.Message);
        }
    }

    private static int RunValidate() => RunValidate(null);

    internal static int RunValidate(string? plansDirectoryOverride)
    {
        try
        {
            var plansDirectory = plansDirectoryOverride ?? Path.Combine(
                FindRepositoryRoot(), "samples", "Sanare.Samples.Lenovo.State", "scripts", "plans", "lenovo-com");
            var serializer = new PlanSerializer();
            var validator = new PlanValidator();

            var detailPlan = serializer.Read(File.ReadAllText(Path.Combine(plansDirectory, "tablet-detail.json")));
            var detailSchema = new SchemaDeriver().Derive<TabletProduct>("nl-NL");
            var detailResult = validator.Validate(detailPlan, detailSchema);

            var listerPlan = serializer.Read(File.ReadAllText(Path.Combine(plansDirectory, "tablet-lister.json")));
            var listerSchema = new SchemaDeriver().Derive<TabletListing>("nl-NL");
            var listerResult = validator.Validate(listerPlan, listerSchema);

            var reports = new[]
            {
                ToReport(detailPlan.SourceId, detailResult),
                ToReport(listerPlan.SourceId, listerResult),
            };

            Console.Out.WriteLine(JsonSerializer.Serialize(reports, LenovoJsonContext.Default.PlanValidationReportArray));
            return reports.All(static report => report.IsValid) ? 0 : 1;
        }
        catch (Exception exception)
        {
            return Fail(exception.Message);
        }
    }

    private static PlanValidationReport ToReport(string sourceId, PlanValidationResult result) => new()
    {
        PlanSourceId = sourceId,
        IsValid = result.IsValid,
        Defects = result.Defects.Select(static defect => $"{defect.PlanPointer}: {defect.Message}").ToArray(),
    };

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
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
