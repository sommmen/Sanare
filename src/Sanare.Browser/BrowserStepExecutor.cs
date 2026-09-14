using Microsoft.Playwright;
using Sanare.Abstractions;
using Sanare.Abstractions.Plans;
using Sanare.Core.Acquisition;

namespace Sanare.Browser;

/// <summary>Runs the plan's declared interaction steps against a live page.</summary>
public interface IBrowserStepExecutor
{
    Task RunAsync(IPage page, IReadOnlyList<InteractionStep> steps, BrowserOptions options, CancellationToken cancellationToken = default);
}

/// <summary>
/// Dispatches the six catalog interaction operations (docs/features/browser-tier.md, T8
/// "interaction executor"). Anything outside the catalog's declared arity/kind is rejected before
/// any step runs; arbitrary <c>page.EvaluateAsync</c> of model-authored script is never supported.
/// </summary>
public sealed class BrowserStepExecutor : IBrowserStepExecutor
{
    public async Task RunAsync(IPage page, IReadOnlyList<InteractionStep> steps, BrowserOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(options);

        for (var index = 0; index < steps.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var step = steps[index];
            Validate(step, index);
            await ExecuteAsync(page, step, index, options, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void Validate(InteractionStep step, int index)
    {
        var descriptor = PlanOperationCatalogLookup(step.Operation, index);
        if (descriptor.Category != PlanOperationCategory.Interaction || !descriptor.AllowedTiers.Contains(AcquisitionTier.Browser))
        {
            throw new AcquisitionException("SNR-BRW-004", $"Step {index} operation '{step.Operation}' is not a browser interaction.");
        }

        var argumentCount = step.Arguments.Count;
        if (argumentCount < descriptor.MinArguments || argumentCount > descriptor.MaxArguments)
        {
            throw new AcquisitionException("SNR-BRW-004", $"Step {index} operation '{step.Operation}' expects {descriptor.MinArguments}-{descriptor.MaxArguments} arguments but received {argumentCount}.");
        }
    }

    private static PlanOperationDescriptor PlanOperationCatalogLookup(PlanOperation operation, int index)
    {
        try
        {
            return PlanOperationCatalog.Get(operation);
        }
        catch (KeyNotFoundException)
        {
            throw new AcquisitionException("SNR-BRW-004", $"Step {index} operation '{operation}' is not a recognized plan operation.");
        }
    }

    private static async Task ExecuteAsync(IPage page, InteractionStep step, int index, BrowserOptions options, CancellationToken cancellationToken)
    {
        try
        {
            switch (step.Operation)
            {
                case PlanOperation.Click:
                    await page.ClickAsync(step.Arguments[0]).ConfigureAwait(false);
                    break;

                case PlanOperation.WaitForSelector:
                    await page.WaitForSelectorAsync(step.Arguments[0], new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible }).ConfigureAwait(false);
                    break;

                case PlanOperation.WaitForNetworkIdle:
                    var quietMillis = step.Arguments.Count > 0 ? (float)ParseInt(step.Arguments[0], index) : (float?)null;
                    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = quietMillis }).ConfigureAwait(false);
                    break;

                case PlanOperation.Scroll:
                    var requested = step.Arguments.Count > 0 ? ParseInt(step.Arguments[0], index) : 1;
                    var times = Math.Min(requested, options.MaxScrolls);
                    for (var i = 0; i < times; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await page.EvaluateAsync("() => window.scrollTo(0, document.body.scrollHeight)").ConfigureAwait(false);
                        if (options.EffectiveScrollDelay > TimeSpan.Zero)
                        {
                            await Task.Delay(options.EffectiveScrollDelay, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    break;

                case PlanOperation.SelectOption:
                    await page.SelectOptionAsync(step.Arguments[0], step.Arguments[1]).ConfigureAwait(false);
                    break;

                case PlanOperation.Type:
                    await page.Locator(step.Arguments[0]).PressSequentiallyAsync(step.Arguments[1]).ConfigureAwait(false);
                    break;

                default:
                    throw new AcquisitionException("SNR-BRW-004", $"Step {index} operation '{step.Operation}' has no browser dispatch implementation.");
            }
        }
        catch (Exception ex) when (ex is not AcquisitionException and not OperationCanceledException)
        {
            var selector = step.Arguments.Count > 0 ? step.Arguments[0] : "<none>";
            throw new AcquisitionException("SNR-BRW-004", $"Step {index} ('{step.Operation}', selector '{selector}') failed: {ex.Message}");
        }
    }

    private static int ParseInt(string value, int index)
    {
        if (!int.TryParse(value, out var parsed))
        {
            throw new AcquisitionException("SNR-BRW-004", $"Step {index} argument '{value}' is not a valid integer.");
        }

        return parsed;
    }
}
