namespace Sanare.Abstractions.Telemetry;

/// <summary>Receives operational alerts raised by Sanare.</summary>
public interface IAlertSink
{
    ValueTask RaiseAsync(AlertRaised alert, CancellationToken cancellationToken = default);
}
