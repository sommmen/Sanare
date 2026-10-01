using System.Reflection;
using Sanare.Core.Acquisition;
using Sanare.Http.Resilience;

namespace Sanare.Http.Tests.Acquisition;

/// <summary>
/// Guards the two structural constraints the acquisition spec states but that no functional test can
/// enforce: that outbound traffic funnels through the governed pipeline, and that no challenge is ever
/// solved in-process.
/// </summary>
public sealed class AcquisitionArchitectureTests
{
    /// <summary>
    /// Types permitted to hold an <see cref="HttpClient"/>. The transport and the robots fetcher sit inside
    /// the acquisition boundary; the factory is the composition root that hands the client to them.
    /// </summary>
    private static readonly HashSet<string> HttpClientHolders = new(StringComparer.Ordinal)
    {
        "Sanare.Core.Acquisition.HttpContentAcquirer",
        "Sanare.Http.Robots.RobotsPolicy",
        "Sanare.Http.Discovery.DiscoveryDocumentResolver",
        "Sanare.Http.AcquisitionPipelineFactory",
    };

    [Fact]
    public void Only_the_acquisition_boundary_holds_an_http_client()
    {
        // Every socket that touches a target host has to pass the limiter, robots, and the breaker. A type
        // that quietly holds its own HttpClient routes around all three, so the field itself is the smell.
        var offenders = new List<string>();

        foreach (var type in ProductionTypes())
        {
            if (HttpClientHolders.Contains(type.FullName ?? type.Name) || IsBrowserTier(type))
            {
                continue;
            }

            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var field in fields)
            {
                if (field.FieldType == typeof(HttpClient) || field.FieldType == typeof(HttpMessageHandler))
                {
                    offenders.Add($"{type.FullName}.{field.Name}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void Only_the_acquisition_boundary_accepts_an_http_client()
    {
        var offenders = new List<string>();

        foreach (var type in ProductionTypes())
        {
            if (HttpClientHolders.Contains(type.FullName ?? type.Name) || IsBrowserTier(type))
            {
                continue;
            }

            foreach (var constructor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(HttpClient)))
                {
                    offenders.Add($"{type.FullName}..ctor");
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void Exactly_the_browser_handoff_implementation_is_present()
    {
        // The hand-off is deliberately an escape hatch for a human operator. There must be exactly one
        // implementation, and its identity is part of the no-solver boundary rather than an open extension
        // point for automation.
        var implementations = ProductionTypes()
            .Where(type => typeof(IChallengeHandoff).IsAssignableFrom(type) && type is { IsInterface: false, IsAbstract: false })
            .ToList();

        var implementation = Assert.Single(implementations);
        Assert.Equal("PlaywrightChallengeHandoff", implementation.Name);
    }

    [Fact]
    public void No_production_type_consumes_the_challenge_handoff()
    {
        var offenders = new List<string>();

        foreach (var type in ProductionTypes())
        {
            foreach (var constructor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(IChallengeHandoff)))
                {
                    offenders.Add($"{type.FullName}..ctor");
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_governed_acquirer_is_substitutable_for_the_bare_transport()
    {
        // The whole point of the decorator is that a consumer holding IContentAcquirer cannot tell the
        // difference, so governance cannot be bypassed by depending on the concrete transport.
        Assert.True(typeof(IContentAcquirer).IsAssignableFrom(typeof(GovernedContentAcquirer)));
        Assert.True(typeof(IContentAcquirer).IsAssignableFrom(typeof(HttpContentAcquirer)));
        Assert.True(typeof(IContentAcquirer).IsAssignableFrom(typeof(TieredContentAcquirer)));
    }

    private static bool IsBrowserTier(Type type) =>
        (type.FullName ?? string.Empty).Contains("Browser", StringComparison.Ordinal);

    private static IEnumerable<Type> ProductionTypes() =>
        new[]
        {
            typeof(GovernedContentAcquirer).Assembly,
            typeof(HttpContentAcquirer).Assembly,
            typeof(Sanare.Browser.BrowserTierGate).Assembly,
        }
            .Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => !type.Name.Contains('<', StringComparison.Ordinal));
}
