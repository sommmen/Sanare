namespace Sanare.Core.Observability;

/// <summary>Thrown when a startup validation guard in this namespace refuses to start.</summary>
public sealed class ObservabilityStartupException : Exception
{
    public ObservabilityStartupException(string message) : base(message)
    {
    }
}

/// <summary>
/// Asserts that GenAI OpenTelemetry instrumentation is attached at exactly one layer (the agent), never
/// also at the underlying <c>IChatClient</c>, per docs/sanare/tech-design.md §13.2 and SNR-OBS-002.
/// </summary>
public static class InstrumentationGuard
{
    /// <summary>
    /// Validates that at most one of the agent-layer and chat-client-layer OpenTelemetry sources is
    /// registered. Enabling both silently duplicates every GenAI span/attribute and double-counts tokens.
    /// </summary>
    public static void ValidateSingleGenAiLayer(bool agentSourceRegistered, bool chatClientSourceRegistered)
    {
        if (agentSourceRegistered && chatClientSourceRegistered)
        {
            throw new ObservabilityStartupException(
                "SNR-OBS-002: OpenTelemetry instrumentation is registered for both the agent " +
                "(Experimental.Microsoft.Agents.AI) and the underlying IChatClient " +
                "(Experimental.Microsoft.Extensions.AI). Attach instrumentation at the agent layer only; " +
                "enabling both duplicates spans and double-counts tokens.");
        }
    }
}
