using System.Text.Json;
using Sanare.Core.Fixtures;
using Sanare.Core.Plans;
using Sanare.Core.Runtime;
using Sanare.Core.Schema;
using Sanare.Core.Schema.Coercion;
using Sanare.Core.Schema.Materialization;

namespace Sanare.Samples.Lenovo;

public static class Program
{
    private const string DetailSourceId = "lenovo-com/tablet-detail";
    private const string DetailUrl = "https://www.lenovo.com/nl/nl/p/tablets/android-tablets/lenovo-tab-series/lenovo-yoga-tab-gen-2/len103y0003";

    public static int Main(string[] args)
    {
        if (args is ["detail", "--offline"])
        {
            return RunDetail();
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

    private static IReadOnlyList<Uri> ExtractImages(string content)
    {
        const string imagePrefix = "\"image\":[\"//";
        var start = content.IndexOf(imagePrefix, StringComparison.Ordinal) + imagePrefix.Length;
        var end = start > imagePrefix.Length ? content.IndexOf('"', start) : -1;
        return end > start ? [new Uri("https://" + content[start..end])] : [];
    }

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
