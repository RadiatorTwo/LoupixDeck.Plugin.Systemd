using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Systemd;

/// <summary>
/// Shared behaviour of every command that acts on a unit: it resolves which unit is meant, runs
/// the action, waits for the systemd job and reports the result on the touch slot. It never
/// throws — a failing command must not take the host's command pipeline with it.
/// </summary>
internal abstract class UnitCommandBase(
    CommandDescriptor descriptor,
    UnitRegistry registry,
    SystemdSettings settings) : IPluginCommand
{
    /// <summary>How long a short confirmation stays on the touch slot.</summary>
    private static readonly TimeSpan OverlayDuration = TimeSpan.FromSeconds(2);

    public CommandDescriptor Descriptor { get; } = descriptor;

    public virtual ButtonTargets SupportedTargets => ButtonTargets.All;

    protected UnitRegistry Registry { get; } = registry;

    protected SystemdSettings Settings { get; } = settings;

    public async Task Execute(CommandContext ctx)
    {
        try
        {
            await ExecuteCore(ctx).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ctx.Host.Logger.Warn($"{Descriptor.CommandName} failed: {ex.Message}");
        }
    }

    protected abstract Task ExecuteCore(CommandContext ctx);

    /// <summary>
    /// The unit a press acts on: the button's own parameter, or the first favorite when the
    /// parameter is empty, so a command dropped on a button without configuring it still works.
    /// </summary>
    protected UnitId ResolveUnit(CommandContext ctx)
    {
        string? parameter = ctx.Parameters.Length > 0 ? ctx.Parameters[0] : null;

        if (!string.IsNullOrWhiteSpace(parameter))
        {
            return UnitId.Parse(parameter, Settings.PreferredDomain);
        }

        return Settings.FavoriteAt(0);
    }

    /// <summary>Runs an action and shows how it ended on the touch slot the press came from.</summary>
    protected async Task RunActionAsync(CommandContext ctx, UnitId id, UnitAction action)
    {
        if (!id.IsValid)
        {
            ShowOverlay(ctx, ctx.Host.Tr("No unit"));
            ctx.Host.Logger.Warn($"{Descriptor.CommandName} has no unit to act on.");
            return;
        }

        UnitActionResult result = await Registry.RunAsync(id, action, Settings.CommandTimeout).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            // A runtime action shows itself on the unit's state; a unit file change does not, so
            // the button says what was written, or that there was nothing to write.
            if (UnitActionParser.IsPersistent(action))
            {
                ShowOverlay(ctx, ctx.Host.Tr(result.Changed ? UnitActionParser.ToDoneText(action) : "Already in that state"));
            }

            return;
        }

        string message = result.JobResult.Length > 0
            ? JobTracker.ToEnglishText(result.JobResult)
            : SystemdErrors.ToEnglishText(result.Outcome);

        ShowOverlay(ctx, ctx.Host.Tr(message));
        ctx.Host.Logger.Info($"{Descriptor.CommandName} on {id}: {message}.");
    }

    /// <summary>
    /// Shows a short message on the touch slot that was pressed. A rotary press has no slot of its
    /// own, so the host is asked for the slot next to it.
    /// </summary>
    protected static void ShowOverlay(CommandContext ctx, string text)
    {
        if (ctx.SourceIndex is not int index)
        {
            return;
        }

        int slot = ctx.Target == ButtonTargets.RotaryEncoder
            ? ctx.Host.GetTouchSlotForRotary(index)
            : index;

        if (slot < 0)
        {
            return;
        }

        ctx.Host.OverlayTouchText(slot, text, OverlayDuration);
    }
}
