using System.Globalization;
using System.Text;
using Sanare.Abstractions.Plans;

namespace Sanare.Core.Repository;

/// <summary>
/// Writes and reads the structured trailer block carried by every plan commit message. Writer and reader
/// share one constant per trailer key so the two cannot drift. See docs/features/script-repository.md
/// ("Commit and bootstrap behavior", "Implementation Plan" → T3).
/// </summary>
internal static class PlanCommitMessage
{
    private const string SchemaKey = "Schema";
    private const string TierKey = "Tier";
    private const string PlanVersionKey = "Plan-Version";
    private const string ScoreKey = "Score";
    private const string FixturesKey = "Fixtures";
    private const string ModelKey = "Model";
    private const string AttemptsKey = "Attempts";
    private const string ReasonKey = "Reason";

    /// <summary>
    /// Renders <paramref name="request"/> as a subject line plus the trailer block <see cref="TryParse"/>
    /// reads back.
    /// </summary>
    public static string Build(PlanCommitRequest request)
    {
        var plan = request.Plan;
        var builder = new StringBuilder();
        builder.Append(request.Verb).Append('(').Append(plan.SourceId).Append('/').Append(plan.SchemaName).Append("): ").Append(request.Summary).Append('\n');
        builder.Append('\n');
        Trailer(builder, SchemaKey, $"{plan.SchemaName}@{plan.SchemaVersion.ToString(CultureInfo.InvariantCulture)} ({plan.SchemaHash})");
        Trailer(builder, TierKey, plan.Tier.ToString());
        Trailer(builder, PlanVersionKey, plan.PlanVersion.ToString(CultureInfo.InvariantCulture));
        Trailer(builder, ScoreKey, plan.Provenance.Score.ToString("0.0000", CultureInfo.InvariantCulture));
        Trailer(builder, FixturesKey, string.Join(", ", plan.Provenance.FixtureIds));
        Trailer(builder, ModelKey, plan.Provenance.Model);
        Trailer(builder, AttemptsKey, plan.Provenance.Attempts.ToString(CultureInfo.InvariantCulture));
        Trailer(builder, ReasonKey, request.Reason);
        return builder.ToString();
    }

    /// <summary>
    /// Reads back whatever provenance <paramref name="message"/>'s trailer block carries, filling the rest of
    /// <paramref name="info"/> from the supplied commit identity. Returns <see langword="false"/> when no
    /// recognizable trailer is present — a hand-written commit still yields an entry, it just carries no
    /// provenance, because the repository must stay readable after a human commits by hand.
    /// </summary>
    public static bool TryParse(
        string? message,
        string commitId,
        string branch,
        string? approvalTag,
        DateTimeOffset committedAt,
        out PlanCommitInfo info)
    {
        var trailers = ReadTrailers(message);

        var schemaName = string.Empty;
        var schemaVersion = 0;
        var schemaHash = string.Empty;
        if (trailers.TryGetValue(SchemaKey, out var schema))
        {
            TryParseSchema(schema, out schemaName, out schemaVersion, out schemaHash);
        }

        var score = 0d;
        if (trailers.TryGetValue(ScoreKey, out var scoreText))
        {
            _ = double.TryParse(scoreText, NumberStyles.Float, CultureInfo.InvariantCulture, out score);
        }

        var attempts = 0;
        if (trailers.TryGetValue(AttemptsKey, out var attemptsText))
        {
            _ = int.TryParse(attemptsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out attempts);
        }

        IReadOnlyList<string> fixtureIds = trailers.TryGetValue(FixturesKey, out var fixtures) && !string.IsNullOrWhiteSpace(fixtures)
            ? fixtures.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];

        info = new PlanCommitInfo(
            commitId,
            branch,
            approvalTag,
            schemaName,
            schemaVersion,
            schemaHash,
            trailers.GetValueOrDefault(ModelKey, string.Empty),
            attempts,
            fixtureIds,
            score,
            trailers.GetValueOrDefault(ReasonKey, string.Empty),
            committedAt);

        return trailers.Count > 0;
    }

    private static void Trailer(StringBuilder builder, string key, string value) =>
        builder.Append(key).Append(": ").Append(value).Append('\n');

    private static Dictionary<string, string> ReadTrailers(string? message)
    {
        var trailers = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(message))
        {
            return trailers;
        }

        foreach (var rawLine in message.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var separator = line.IndexOf(": ", StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator];
            if (!IsKnownKey(key))
            {
                continue;
            }

            trailers[key] = line[(separator + 2)..].Trim();
        }

        return trailers;
    }

    private static bool IsKnownKey(string key) => key switch
    {
        SchemaKey or TierKey or PlanVersionKey or ScoreKey or FixturesKey or ModelKey or AttemptsKey or ReasonKey => true,
        _ => false,
    };

    private static void TryParseSchema(string value, out string name, out int version, out string hash)
    {
        name = string.Empty;
        version = 0;
        hash = string.Empty;

        // "Product@1 (abc123)"
        var at = value.LastIndexOf('@');
        if (at <= 0)
        {
            return;
        }

        name = value[..at];
        var remainder = value[(at + 1)..];

        var openParen = remainder.IndexOf(" (", StringComparison.Ordinal);
        if (openParen >= 0)
        {
            var closeParen = remainder.LastIndexOf(')');
            if (closeParen > openParen + 1)
            {
                hash = remainder[(openParen + 2)..closeParen];
            }

            remainder = remainder[..openParen];
        }

        _ = int.TryParse(remainder.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out version);
    }
}
