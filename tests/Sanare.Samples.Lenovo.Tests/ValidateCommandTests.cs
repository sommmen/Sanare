using System.Text.Json;
using Sanare.Samples.Lenovo;

namespace Sanare.Samples.Lenovo.Tests;

public sealed class ValidateCommandTests
{
    [Fact]
    public void Validate_reports_both_committed_plans_as_valid_and_exits_zero()
    {
        lock (ConsoleTestLock.Instance)
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var originalOut = Console.Out;
            var originalError = Console.Error;
            try
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);
                Assert.Equal(0, Program.Main(["validate"]));
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }

            using var document = JsonDocument.Parse(stdout.ToString());
            var reports = document.RootElement.EnumerateArray().ToArray();
            Assert.Equal(2, reports.Length);
            Assert.All(reports, static report => Assert.True(report.GetProperty("isValid").GetBoolean()));
            Assert.Equal(
                ["lenovo-com/tablet-detail", "lenovo-com/tablet-lister"],
                reports.Select(static report => report.GetProperty("planSourceId").GetString()).Order(StringComparer.Ordinal));
            Assert.Equal(string.Empty, stderr.ToString());
        }
    }

    [Fact]
    public void Validate_fails_a_corrupted_temp_copy_of_the_detail_plan_with_a_nonzero_exit()
    {
        var sourceDirectory = Path.Combine(FindRepositoryRoot(), "samples", "Sanare.Samples.Lenovo.State", "scripts", "plans", "lenovo-com");
        var tempDirectory = Directory.CreateTempSubdirectory("sanare-validate-test-");
        try
        {
            File.Copy(Path.Combine(sourceDirectory, "tablet-lister.json"), Path.Combine(tempDirectory.FullName, "tablet-lister.json"));

            var corrupted = File.ReadAllText(Path.Combine(sourceDirectory, "tablet-detail.json"))
                .Replace("\"pointer\": \"/Name\"", "\"pointer\": \"/DoesNotExistInSchema\"", StringComparison.Ordinal);
            File.WriteAllText(Path.Combine(tempDirectory.FullName, "tablet-detail.json"), corrupted);

            lock (ConsoleTestLock.Instance)
            {
                var stdout = new StringWriter();
                var stderr = new StringWriter();
                var originalOut = Console.Out;
                var originalError = Console.Error;
                try
                {
                    Console.SetOut(stdout);
                    Console.SetError(stderr);
                    Assert.NotEqual(0, Program.RunValidate(tempDirectory.FullName));
                }
                finally
                {
                    Console.SetOut(originalOut);
                    Console.SetError(originalError);
                }

                using var document = JsonDocument.Parse(stdout.ToString());
                var reports = document.RootElement.EnumerateArray().ToArray();
                var detailReport = reports.Single(report => report.GetProperty("planSourceId").GetString() == "lenovo-com/tablet-detail");
                Assert.False(detailReport.GetProperty("isValid").GetBoolean());
                Assert.NotEmpty(detailReport.GetProperty("defects").EnumerateArray());
            }
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
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
