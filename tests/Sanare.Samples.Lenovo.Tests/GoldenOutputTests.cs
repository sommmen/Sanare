using System.Text.Json;
using Sanare.Samples.Lenovo;

namespace Sanare.Samples.Lenovo.Tests;

/// <summary>
/// Pins each offline command's stdout to its committed golden file. These are the tests that make a
/// silent extraction regression — a plan that still validates and still exits 0 but quietly stops
/// finding a field — fail the build instead of shipping.
/// </summary>
public sealed class GoldenOutputTests
{
    [Fact]
    public void Detail_offline_matches_its_golden_output()
    {
        var actual = RunCommand(["detail", "--offline"]);

        AssertSemanticallyEqual(ReadGolden("yoga-tab-gen2.json"), actual);
    }

    [Fact]
    public void List_offline_matches_its_golden_output()
    {
        var actual = RunCommand(["list", "--offline"]);

        AssertSemanticallyEqual(ReadGolden("tablet-list.json"), actual);
    }

    [Fact]
    public void Detail_golden_carries_the_whole_specification_table()
    {
        using var document = JsonDocument.Parse(ReadGolden("yoga-tab-gen2.json"));
        var root = document.RootElement;

        Assert.Equal("Lenovo Yoga Tab Gen 2", root.GetProperty("name").GetString());
        Assert.Equal(649.01m, root.GetProperty("price").GetDecimal());
        Assert.Equal("EUR", root.GetProperty("currency").GetString());

        var specifications = root.GetProperty("specifications").EnumerateArray().ToArray();
        Assert.True(specifications.Length >= 14, $"Expected at least 14 specification rows; found {specifications.Length}.");
        Assert.All(specifications, specification =>
        {
            Assert.False(string.IsNullOrWhiteSpace(specification.GetProperty("name").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(specification.GetProperty("value").GetString()));
        });
    }

    private static string RunCommand(string[] args)
    {
        lock (ConsoleTestLock.Instance)
        {
            var stdout = new StringWriter();
            var originalOut = Console.Out;
            try
            {
                Console.SetOut(stdout);
                Assert.Equal(0, Program.Main(args));
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            return stdout.ToString();
        }
    }

    /// <summary>
    /// Compares JSON semantics rather than bytes, so that insignificant whitespace or a trailing
    /// newline difference between the committed file and the writer never fails the build.
    /// </summary>
    private static void AssertSemanticallyEqual(string expected, string actual)
    {
        using var expectedDocument = JsonDocument.Parse(expected);
        using var actualDocument = JsonDocument.Parse(actual);

        Assert.Equal(
            JsonSerializer.Serialize(expectedDocument.RootElement),
            JsonSerializer.Serialize(actualDocument.RootElement));
    }

    private static string ReadGolden(string fileName) =>
        File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "samples", "Sanare.Samples.Lenovo.State", "golden", fileName));

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
