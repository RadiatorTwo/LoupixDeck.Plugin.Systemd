using System.Collections.Concurrent;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Systemd;

/// <summary>
/// One runtime action on one unit. The unit comes from the button's parameter, so the same command
/// serves every unit; the declared states stay off it, because the host binds a state to a command
/// name alone and two buttons with different units would then share one state.
/// </summary>
internal sealed class UnitActionCommand(
    CommandDescriptor descriptor,
    UnitAction action,
    UnitRegistry registry,
    SystemdSettings settings) : UnitCommandBase(descriptor, registry, settings)
{
    protected override Task ExecuteCore(CommandContext ctx) => RunActionAsync(ctx, ResolveUnit(ctx), action);
}

/// <summary>
/// One persistent action on one unit. It runs only with the opt-in in the settings and, unless
/// the user turned that off, only on a second press within a few seconds of the first: a unit
/// file change survives a reboot, so a stray press must not be enough.
/// </summary>
internal sealed class PersistentActionCommand(
    CommandDescriptor descriptor,
    UnitAction action,
    UnitRegistry registry,
    SystemdSettings settings) : UnitCommandBase(descriptor, registry, settings)
{
    /// <summary>How long the first press waits for the confirming second one.</summary>
    private static readonly TimeSpan ConfirmWindow = TimeSpan.FromSeconds(3);

    /// <summary>When each unit was pressed first, keyed by unit, since one command serves many buttons.</summary>
    private readonly ConcurrentDictionary<UnitId, long> _armed = [];

    protected override Task ExecuteCore(CommandContext ctx)
    {
        UnitId id = ResolveUnit(ctx);

        if (!Settings.AllowPersistentActions)
        {
            ShowOverlay(ctx, ctx.Host.Tr("Persistent actions are off"));
            ctx.Host.Logger.Info($"{Descriptor.CommandName} on {id} ignored: persistent actions are not allowed in the settings.");
            return Task.CompletedTask;
        }

        if (id.IsValid && Settings.ConfirmPersistentActions && !Confirm(id))
        {
            ShowOverlay(ctx, ctx.Host.Tr("Press again to confirm"));
            return Task.CompletedTask;
        }

        return RunActionAsync(ctx, id, action);
    }

    /// <summary>
    /// True on the second press inside the window. The first press arms the unit and returns
    /// false; a press after the window has run out arms it again.
    /// </summary>
    private bool Confirm(UnitId id)
    {
        long now = Environment.TickCount64;

        if (_armed.TryRemove(id, out long armedAt) && now - armedAt <= (long)ConfirmWindow.TotalMilliseconds)
        {
            return true;
        }

        _armed[id] = now;
        return false;
    }
}

/// <summary>Builds the runtime and persistent commands the plugin publishes.</summary>
internal static class UnitActionCommands
{
    public static IEnumerable<IPluginCommand> Create(UnitRegistry registry, SystemdSettings settings)
    {
        yield return Build(
            SystemdCommands.Start,
            "Start Unit",
            "Start the unit and wait for systemd to report the result",
            UnitAction.Start,
            registry,
            settings);

        yield return Build(
            SystemdCommands.Stop,
            "Stop Unit",
            "Stop the unit and wait for systemd to report the result",
            UnitAction.Stop,
            registry,
            settings);

        yield return Build(
            SystemdCommands.Restart,
            "Restart Unit",
            "Restart the unit and wait for systemd to report the result",
            UnitAction.Restart,
            registry,
            settings);

        yield return Build(
            SystemdCommands.Reload,
            "Reload Unit",
            "Ask the unit to reload its configuration without restarting it",
            UnitAction.Reload,
            registry,
            settings);

        yield return Build(
            SystemdCommands.Toggle,
            "Toggle Unit",
            "Stop the unit when it runs, start it when it does not",
            UnitAction.Toggle,
            registry,
            settings);

        yield return Build(
            SystemdCommands.ResetFailed,
            "Reset Failed Unit",
            "Clear the failed state of the unit without starting or stopping it",
            UnitAction.ResetFailed,
            registry,
            settings);

        // The persistent actions carry their nature in the name, so the picker never shows
        // "Disable Unit" next to "Stop Unit" as if the two were alike.
        yield return BuildPersistent(
            SystemdCommands.Enable,
            "Enable Unit (persistent)",
            "Make the unit start on its own at boot or login. Changes the unit file state and survives a reboot; needs Allow persistent actions in the settings.",
            UnitAction.Enable,
            registry,
            settings);

        yield return BuildPersistent(
            SystemdCommands.Disable,
            "Disable Unit (persistent)",
            "Stop the unit from starting on its own. Changes the unit file state and survives a reboot; needs Allow persistent actions in the settings.",
            UnitAction.Disable,
            registry,
            settings);

        yield return BuildPersistent(
            SystemdCommands.Mask,
            "Mask Unit (persistent)",
            "Block the unit so nothing can start it, not even by hand. Changes the unit file state and survives a reboot; needs Allow persistent actions in the settings.",
            UnitAction.Mask,
            registry,
            settings);

        yield return BuildPersistent(
            SystemdCommands.Unmask,
            "Unmask Unit (persistent)",
            "Lift a mask so the unit can be started again. Changes the unit file state and survives a reboot; needs Allow persistent actions in the settings.",
            UnitAction.Unmask,
            registry,
            settings);
    }

    private static IPluginCommand BuildPersistent(
        string commandName,
        string displayName,
        string description,
        UnitAction action,
        UnitRegistry registry,
        SystemdSettings settings)
    {
        CommandDescriptor descriptor = SystemdCommands.UnitDescriptor(commandName, displayName, description);
        return new PersistentActionCommand(descriptor, action, registry, settings);
    }

    private static IPluginCommand Build(
        string commandName,
        string displayName,
        string description,
        UnitAction action,
        UnitRegistry registry,
        SystemdSettings settings)
    {
        CommandDescriptor descriptor = SystemdCommands.UnitDescriptor(commandName, displayName, description);
        return new UnitActionCommand(descriptor, action, registry, settings);
    }
}
