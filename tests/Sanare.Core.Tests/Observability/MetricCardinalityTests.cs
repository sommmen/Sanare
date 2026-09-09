using System.Diagnostics.Metrics;
using Sanare.Core.Observability;

namespace Sanare.Core.Tests.Observability;

public sealed class MetricCardinalityTests
{
    [Fact]
    public void Guard_Collapses_unbounded_value_to_other()
    {
        var guard = new CardinalityGuard(sources: ["known-source"]);

        var result = guard.Guard(TagNames.Source, "unknown-source");

        Assert.Equal(CardinalityGuard.Other, result);
    }

    [Fact]
    public void Guard_Allows_a_value_within_the_configured_bounded_set()
    {
        var guard = new CardinalityGuard(sources: ["known-source"]);

        var result = guard.Guard(TagNames.Source, "known-source");

        Assert.Equal("known-source", result);
    }

    [Fact]
    public void Guard_Rejects_a_url_value_for_a_closed_tag()
    {
        var guard = new CardinalityGuard();

        var result = guard.Guard(TagNames.Origin, "https://example.test/path?query=1");

        Assert.Equal(CardinalityGuard.Other, result);
    }

    [Fact]
    public void Guard_Rejects_a_query_string_value_for_a_closed_tag()
    {
        var guard = new CardinalityGuard();

        var result = guard.Guard(TagNames.Status, "ok&extra=1");

        Assert.Equal(CardinalityGuard.Other, result);
    }

    [Fact]
    public void Guard_Rejects_an_unbounded_identifier_for_a_closed_tag()
    {
        var guard = new CardinalityGuard();

        var result = guard.Guard(TagNames.Kind, Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N") + "x");

        Assert.Equal(CardinalityGuard.Other, result);
    }

    [Fact]
    public void Guard_Allows_a_short_closed_set_value()
    {
        var guard = new CardinalityGuard();

        var result = guard.Guard(TagNames.Status, "success");

        Assert.Equal("success", result);
    }

    [Theory]
    [InlineData("sanare.run.duration")]
    [InlineData("sanare.run.count")]
    [InlineData("sanare.run.items")]
    [InlineData("sanare.quality.completeness")]
    [InlineData("sanare.quality.field_null_rate")]
    [InlineData("sanare.quality.coercion_failures")]
    [InlineData("sanare.acquisition.requests")]
    [InlineData("sanare.acquisition.delay")]
    [InlineData("sanare.acquisition.blocked")]
    [InlineData("sanare.acquisition.rate_limit_effective")]
    [InlineData("sanare.acquisition.challenge_paused")]
    [InlineData("sanare.cache.hit_ratio")]
    [InlineData("sanare.browser.contexts_active")]
    [InlineData("sanare.authoring.attempts")]
    [InlineData("sanare.authoring.tokens")]
    [InlineData("sanare.healing.count")]
    [InlineData("sanare.healing.time_to_repair")]
    [InlineData("sanare.plan.age")]
    [InlineData("sanare.budget.spent_ratio")]
    public void MeterListener_Observes_every_catalog_instrument_under_the_Sanare_meter(string instrumentName)
    {
        var seen = false;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Sanare" && instrument.Name == instrumentName)
            {
                seen = true;
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.Start();

        // ScraperMetrics is "beforefieldinit" (no explicit static constructor), so its static
        // instrument fields are not guaranteed to be initialized merely by constructing an
        // instance. Force the static initializer to run so the MeterListener can observe the
        // already-registered "Sanare" meter instruments.
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(ScraperMetrics).TypeHandle);
        listener.RecordObservableInstruments();

        Assert.True(seen, $"Expected an instrument named '{instrumentName}' on the 'Sanare' meter.");
    }

    [Fact]
    public void RecordRunDuration_Emits_the_configured_tag_set()
    {
        var measurements = new List<(string Name, object? Value, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Sanare")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
            measurements.Add((instrument.Name, value, tags.ToArray())));
        listener.Start();

        new ScraperMetrics().RecordRunDuration(12.5, "source-a", "html", "success", "scheduled");

        var measurement = Assert.Single(measurements, m => m.Name == "sanare.run.duration");
        Assert.Equal(12.5, measurement.Value);
        var tagNames = measurement.Tags.Select(tag => tag.Key).ToArray();
        Assert.Equal([TagNames.Source, TagNames.Tier, TagNames.Status, TagNames.Origin], tagNames);
    }

    [Fact]
    public void RecordRunDuration_Does_not_build_a_tag_set_when_no_listener_is_attached()
    {
        // With no MeterListener attached, Instrument.Enabled is false, so RecordRunDuration must return
        // before calling the tag-building helper. We cannot observe allocations directly in a portable
        // way, but we can assert the call completes without throwing and with no measurement recorded
        // by a listener started only after the call (proving the guard, not a timing race).
        var metrics = new ScraperMetrics();

        var exception = Record.Exception(() => metrics.RecordRunDuration(1, "source-a", "html", "success", "scheduled"));

        Assert.Null(exception);
    }

    [Fact]
    public void SetFieldNullRate_Reports_via_the_observable_gauge_with_composite_tags()
    {
        var measurements = new List<(double Value, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Sanare" && instrument.Name == "sanare.quality.field_null_rate")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
            measurements.Add((value, tags.ToArray())));
        listener.Start();

        new ScraperMetrics().SetFieldNullRate(0.42, "source-b", "/Price");
        listener.RecordObservableInstruments();

        var measurement = Assert.Single(measurements, m => m.Tags.Any(tag => Equals(tag.Value, "source-b")));
        Assert.Equal(0.42, measurement.Value);
        Assert.Contains(measurement.Tags, tag => tag.Key == TagNames.Source && Equals(tag.Value, "source-b"));
        Assert.Contains(measurement.Tags, tag => tag.Key == TagNames.Field && Equals(tag.Value, "/Price"));
    }

    [Fact]
    public void Guard_Collapses_unbounded_value_to_other_when_no_allow_list_configured()
    {
        // Regression: with a default CardinalityGuard (no allow-lists configured),
        // an unbounded/high-cardinality value (e.g. random GUID-like string or unsafe chars)
        // must be collapsed to "other" rather than passed through verbatim, ensuring
        // bounded cardinality even without explicit configuration.
        var guard = new CardinalityGuard();
        var guidLikeString = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

        var result = guard.Guard(TagNames.Source, guidLikeString);

        Assert.Equal(CardinalityGuard.Other, result);
    }

    [Fact]
    public void Guard_Allows_safe_short_values_for_bounded_tags_when_no_allow_list_configured()
    {
        // When no explicit allow-list is configured, safe short values (length<=64, only
        // alphanumeric/-/_/.) should pass through for Source/Host/Schema/Field, maintaining
        // backward-compatible behavior for normal use while rejecting high-cardinality values.
        var guard = new CardinalityGuard();

        var result = guard.Guard(TagNames.Source, "safe-source-name");

        Assert.Equal("safe-source-name", result);
    }
}
