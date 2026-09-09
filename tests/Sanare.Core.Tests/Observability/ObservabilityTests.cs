using Microsoft.Extensions.Logging;
using Sanare.Abstractions.Telemetry;
using Sanare.Core.Observability;

namespace Sanare.Core.Tests.Observability;

public sealed class ObservabilityTests
{
    [Fact]
    public void ScraperTelemetry_Names_a_single_ActivitySource_and_a_single_Meter_both_Sanare()
    {
        // Hard constraint: exactly one ActivitySource and one Meter, both named "Sanare".
        Assert.Equal("Sanare", ScraperTelemetry.ActivitySourceName);
        Assert.Equal("Sanare", ScraperTelemetry.MeterName);
    }

    [Fact]
    public void ValidateSingleGenAiLayer_Does_not_throw_when_neither_layer_is_registered()
    {
        var exception = Record.Exception(() => InstrumentationGuard.ValidateSingleGenAiLayer(false, false));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ValidateSingleGenAiLayer_Does_not_throw_when_exactly_one_layer_is_registered(bool agentRegistered, bool chatClientRegistered)
    {
        var exception = Record.Exception(
            () => InstrumentationGuard.ValidateSingleGenAiLayer(agentRegistered, chatClientRegistered));

        Assert.Null(exception);
    }

    [Fact]
    public void ValidateSingleGenAiLayer_Throws_when_both_the_agent_and_chat_client_layers_are_registered()
    {
        // AC-OB-001: GenAI instrumentation must be attached at exactly one layer.
        var exception = Assert.Throws<ObservabilityStartupException>(
            () => InstrumentationGuard.ValidateSingleGenAiLayer(agentSourceRegistered: true, chatClientSourceRegistered: true));

        Assert.Contains("SNR-OBS-002", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SensitiveDataGate_Resolve_Returns_false_when_not_requested()
    {
        var gate = new SensitiveDataGate();

        Assert.False(gate.Resolve(requested: false, stateRootIsEncrypted: true));
        Assert.False(gate.Resolve(requested: false, stateRootIsEncrypted: false));
    }

    [Fact]
    public void SensitiveDataGate_Resolve_Allows_the_request_when_the_state_root_is_encrypted()
    {
        var gate = new SensitiveDataGate();

        Assert.True(gate.Resolve(requested: true, stateRootIsEncrypted: true));
    }

    [Fact]
    public void SensitiveDataGate_Resolve_Refuses_and_logs_when_the_state_root_is_not_encrypted()
    {
        // AC-OB-006: EnableSensitiveData is gated behind an encrypted state root, and the refusal is logged.
        var logger = new CapturingLogger<SensitiveDataGate>();
        var gate = new SensitiveDataGate(logger);

        var resolved = gate.Resolve(requested: true, stateRootIsEncrypted: false);

        Assert.False(resolved);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("SNR-OBS-003", entry.Message, StringComparison.Ordinal);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
