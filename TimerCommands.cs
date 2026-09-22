using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Systemd;

/// <summary>
/// Starts the unit a timer triggers, right now, without waiting for the timer. That is what
/// "run the backup now" means on a deck; starting the timer itself only arms it.
/// </summary>
internal sealed class RunTimerUnitCommand(
    CommandDescriptor descriptor,
    UnitRegistry registry,
    SystemdSettings settings) : UnitCommandBase(descriptor, registry, settings)
{
    protected override async Task ExecuteCore(CommandContext ctx)
    {
        UnitId timer = ResolveUnit(ctx);

        if (!timer.IsValid)
        {
            await RunActionAsync(ctx, timer, UnitAction.Start).ConfigureAwait(false);
            return;
        }

        if (!TimerUnits.IsTimer(timer))
        {
            ShowOverlay(ctx, ctx.Host.Tr("Not a timer"));
            ctx.Host.Logger.Info($"{Descriptor.CommandName} on {timer} ignored: the unit is not a timer.");
            return;
        }

        // The triggered unit is a property of the timer, so it is read first when the timer has
        // not been read yet; the cached value is good enough otherwise, it never changes at runtime.
        UnitState state = Registry.Get(timer);

        if (state.TriggerUnit.Length == 0)
        {
            await Registry.RefreshAsync(timer).ConfigureAwait(false);
            state = Registry.Get(timer);
        }

        UnitId target = new(timer.Domain, TimerUnits.TriggeredUnit(state));

        // Starting the unit by hand leaves the timer's own schedule alone, exactly like
        // "systemctl start" on the service; the timer's last trigger does not move.
        await RunActionAsync(ctx, target, UnitAction.Start).ConfigureAwait(false);
    }
}

/// <summary>Builds the timer commands and holds the rules they share.</summary>
internal static class TimerUnits
{
    public const string RunNow = SystemdCommands.Prefix + "RunTimerUnit";

    public static bool IsTimer(UnitId id) =>
        string.Equals(UnitTypeParser.Of(id.Name), UnitTypeParser.TimerValue, StringComparison.Ordinal);

    /// <summary>
    /// The unit a timer starts. systemd reports it; while it has not been read, the default
    /// systemd itself applies stands in: the service with the timer's name.
    /// </summary>
    public static string TriggeredUnit(UnitState timer)
    {
        if (timer.TriggerUnit.Length > 0)
        {
            return timer.TriggerUnit;
        }

        string name = timer.Id.Name;
        return name[..^(UnitTypeParser.TimerValue.Length + 1)] + "." + UnitTypeParser.ServiceValue;
    }

    public static IPluginCommand CreateRunNow(UnitRegistry registry, SystemdSettings settings)
    {
        CommandDescriptor descriptor = SystemdCommands.UnitDescriptor(
            RunNow,
            "Run Timer Now",
            "Start the unit a timer triggers right away, without waiting for the timer");
        return new RunTimerUnitCommand(descriptor, registry, settings);
    }
}
