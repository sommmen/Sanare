using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Sanare.Abstractions.Telemetry;

namespace Sanare.Core.Observability;

/// <summary>Owns the complete bounded Sanare metric catalog.</summary>
public sealed class ScraperMetrics
{
    private static readonly Meter Meter = new(ScraperTelemetry.MeterName);
    private static readonly Histogram<double> RunDurationInstrument = Meter.CreateHistogram<double>("sanare.run.duration", "ms");
    private static readonly Counter<long> RunCountInstrument = Meter.CreateCounter<long>("sanare.run.count");
    private static readonly Histogram<long> RunItemsInstrument = Meter.CreateHistogram<long>("sanare.run.items");
    private static readonly Histogram<double> CompletenessInstrument = Meter.CreateHistogram<double>("sanare.quality.completeness");
    private static readonly Counter<long> CoercionFailuresInstrument = Meter.CreateCounter<long>("sanare.quality.coercion_failures");
    private static readonly Counter<long> RequestsInstrument = Meter.CreateCounter<long>("sanare.acquisition.requests");
    private static readonly Histogram<double> DelayInstrument = Meter.CreateHistogram<double>("sanare.acquisition.delay", "ms");
    private static readonly Counter<long> BlockedInstrument = Meter.CreateCounter<long>("sanare.acquisition.blocked");
    private static readonly Counter<long> ChallengePausedInstrument = Meter.CreateCounter<long>("sanare.acquisition.challenge_paused");
    private static readonly UpDownCounter<long> BrowserContextsInstrument = Meter.CreateUpDownCounter<long>("sanare.browser.contexts_active");
    private static readonly Histogram<long> AuthoringAttemptsInstrument = Meter.CreateHistogram<long>("sanare.authoring.attempts");
    private static readonly Counter<long> AuthoringTokensInstrument = Meter.CreateCounter<long>("sanare.authoring.tokens");
    private static readonly Counter<long> HealingCountInstrument = Meter.CreateCounter<long>("sanare.healing.count");
    private static readonly Histogram<double> TimeToRepairInstrument = Meter.CreateHistogram<double>("sanare.healing.time_to_repair", "s");
    private static readonly ConcurrentDictionary<string, double> FieldNullRates = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, double> RateLimits = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, double> CacheHitRatios = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, double> PlanAges = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, double> BudgetRatios = new(StringComparer.Ordinal);

    private readonly CardinalityGuard _guard;

    // The five observable gauges are registered once per process via these static field initializers
    // (rather than in the instance constructor), because .NET Meter instruments persist for the
    // lifetime of the Meter: registering them again for every ScraperMetrics instance would create
    // duplicate instruments under the single "Sanare" Meter and duplicate emitted measurements.
    private static readonly ObservableGauge<double> FieldNullRateGauge =
        Meter.CreateObservableGauge("sanare.quality.field_null_rate", ObserveFieldNullRates);
    private static readonly ObservableGauge<double> RateLimitGauge =
        Meter.CreateObservableGauge("sanare.acquisition.rate_limit_effective", ObserveRateLimits, "requests/minute");
    private static readonly ObservableGauge<double> CacheHitRatioGauge =
        Meter.CreateObservableGauge("sanare.cache.hit_ratio", ObserveCacheHitRatios);
    private static readonly ObservableGauge<double> PlanAgeGauge =
        Meter.CreateObservableGauge("sanare.plan.age", ObservePlanAges, "days");
    private static readonly ObservableGauge<double> BudgetSpentRatioGauge =
        Meter.CreateObservableGauge("sanare.budget.spent_ratio", ObserveBudgetRatios);

    public ScraperMetrics(CardinalityGuard? guard = null)
    {
        _guard = guard ?? new CardinalityGuard();
    }

    public void RecordRunDuration(double milliseconds, string source, string tier, string status, string origin)
    {
        if (!RunDurationInstrument.Enabled) return;
        Record(RunDurationInstrument, milliseconds, Tags((TagNames.Source, source), (TagNames.Tier, tier), (TagNames.Status, status), (TagNames.Origin, origin)));
    }

    public void RecordRun(string source, string status)
    {
        if (!RunCountInstrument.Enabled) return;
        Add(RunCountInstrument, Tags((TagNames.Source, source), (TagNames.Status, status)));
    }

    public void RecordRunItems(long items, string source)
    {
        if (!RunItemsInstrument.Enabled) return;
        Record(RunItemsInstrument, items, Tags((TagNames.Source, source)));
    }

    public void RecordCompleteness(double value, string source, string schema)
    {
        if (!CompletenessInstrument.Enabled) return;
        Record(CompletenessInstrument, value, Tags((TagNames.Source, source), (TagNames.Schema, schema)));
    }

    public void RecordCoercionFailure(string source, string field)
    {
        if (!CoercionFailuresInstrument.Enabled) return;
        Add(CoercionFailuresInstrument, Tags((TagNames.Source, source), (TagNames.Field, field)));
    }

    public void RecordRequest(string host, string statusClass, string tier)
    {
        if (!RequestsInstrument.Enabled) return;
        Add(RequestsInstrument, Tags((TagNames.Host, host), (TagNames.StatusClass, statusClass), (TagNames.Tier, tier)));
    }

    public void RecordDelay(double milliseconds, string host, string reason)
    {
        if (!DelayInstrument.Enabled) return;
        Record(DelayInstrument, milliseconds, Tags((TagNames.Host, host), (TagNames.Reason, reason)));
    }

    public void RecordBlocked(string host, string kind)
    {
        if (!BlockedInstrument.Enabled) return;
        Add(BlockedInstrument, Tags((TagNames.Host, host), (TagNames.Kind, kind)));
    }

    public void RecordChallengePaused(string host)
    {
        if (!ChallengePausedInstrument.Enabled) return;
        Add(ChallengePausedInstrument, Tags((TagNames.Host, host)));
    }

    public void RecordBrowserContexts(long delta) { if (BrowserContextsInstrument.Enabled) BrowserContextsInstrument.Add(delta); }

    public void RecordAuthoringAttempts(long attempts, string source, string outcome, string modelProfile)
    {
        if (!AuthoringAttemptsInstrument.Enabled) return;
        Record(AuthoringAttemptsInstrument, attempts, Tags((TagNames.Source, source), (TagNames.Outcome, outcome), (TagNames.ModelProfile, modelProfile)));
    }

    public void RecordAuthoringTokens(long tokens, string source, string direction, string modelProfile)
    {
        if (!AuthoringTokensInstrument.Enabled) return;
        Add(AuthoringTokensInstrument, Tags((TagNames.Source, source), (TagNames.Direction, direction), (TagNames.ModelProfile, modelProfile)), tokens);
    }

    public void RecordHealing(string source, string classification, string outcome, string modelProfile)
    {
        if (!HealingCountInstrument.Enabled) return;
        Add(HealingCountInstrument, Tags((TagNames.Source, source), (TagNames.Classification, classification), (TagNames.Outcome, outcome), (TagNames.ModelProfile, modelProfile)));
    }

    public void RecordTimeToRepair(double seconds, string source)
    {
        if (!TimeToRepairInstrument.Enabled) return;
        Record(TimeToRepairInstrument, seconds, Tags((TagNames.Source, source)));
    }
    public void SetFieldNullRate(double value, string source, string field) => FieldNullRates[$"{_guard.Guard(TagNames.Source, source)}|{_guard.Guard(TagNames.Field, field)}"] = value;
    public void SetEffectiveRateLimit(double value, string host) => RateLimits[_guard.Guard(TagNames.Host, host)] = value;
    public void SetCacheHitRatio(double value, string layer) => CacheHitRatios[_guard.Guard(TagNames.Layer, layer)] = value;
    public void SetPlanAge(double value, string source) => PlanAges[_guard.Guard(TagNames.Source, source)] = value;
    public void SetBudgetSpentRatio(double value, string source) => BudgetRatios[_guard.Guard(TagNames.Source, source)] = value;

    private TagList Tags(params (string Name, string Value)[] values)
    {
        var tags = new TagList();
        foreach (var (name, value) in values)
        {
            tags.Add(name, _guard.Guard(name, value));
        }
        return tags;
    }

    private static void Add(Counter<long> instrument, TagList tags, long value = 1) { if (instrument.Enabled) instrument.Add(value, tags); }
    private static void Record(Histogram<double> instrument, double value, TagList tags) { if (instrument.Enabled) instrument.Record(value, tags); }
    private static void Record(Histogram<long> instrument, long value, TagList tags) { if (instrument.Enabled) instrument.Record(value, tags); }
    private static IEnumerable<Measurement<double>> ObserveFieldNullRates() => ObserveComposite(FieldNullRates, TagNames.Source, TagNames.Field);
    private static IEnumerable<Measurement<double>> ObserveRateLimits() => ObserveSingle(RateLimits, TagNames.Host);
    private static IEnumerable<Measurement<double>> ObserveCacheHitRatios() => ObserveSingle(CacheHitRatios, TagNames.Layer);
    private static IEnumerable<Measurement<double>> ObservePlanAges() => ObserveSingle(PlanAges, TagNames.Source);
    private static IEnumerable<Measurement<double>> ObserveBudgetRatios() => ObserveSingle(BudgetRatios, TagNames.Source);
    private static IEnumerable<Measurement<double>> ObserveSingle(IEnumerable<KeyValuePair<string, double>> values, string tag) => values.Select(pair => new Measurement<double>(pair.Value, new KeyValuePair<string, object?>(tag, pair.Key)));
    private static IEnumerable<Measurement<double>> ObserveComposite(IEnumerable<KeyValuePair<string, double>> values, string firstTag, string secondTag) => values.Select(pair =>
    {
        var parts = pair.Key.Split('|', 2);
        var tags = new TagList { { firstTag, parts[0] }, { secondTag, parts[1] } };
        return new Measurement<double>(pair.Value, tags);
    });
}
