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

/// <summary>Builds the runtime commands the plugin publishes.</summary>
internal static class UnitActionCommands
{
    public static IEnumerable<IPluginCommand> Create(UnitRegistry registry, SystemdSettings settings)
    {
        yield return Build(
            SystemdCommands.Start,
            "Start Unit",
            "Start the unit and wait for systemd to report the result",
            SystemdCommands.PlayGlyph,
            UnitAction.Start,
            registry,
            settings);

        yield return Build(
            SystemdCommands.Stop,
            "Stop Unit",
            "Stop the unit and wait for systemd to report the result",
            SystemdCommands.StopGlyph,
            UnitAction.Stop,
            registry,
            settings);

        yield return Build(
            SystemdCommands.Restart,
            "Restart Unit",
            "Restart the unit and wait for systemd to report the result",
            SystemdCommands.RestartGlyph,
            UnitAction.Restart,
            registry,
            settings);

        yield return Build(
            SystemdCommands.Reload,
            "Reload Unit",
            "Ask the unit to reload its configuration without restarting it",
            SystemdCommands.RefreshGlyph,
            UnitAction.Reload,
            registry,
            settings);

        yield return Build(
            SystemdCommands.Toggle,
            "Toggle Unit",
            "Stop the unit when it runs, start it when it does not",
            SystemdCommands.PowerGlyph,
            UnitAction.Toggle,
            registry,
            settings);

        yield return Build(
            SystemdCommands.ResetFailed,
            "Reset Failed Unit",
            "Clear the failed state of the unit without starting or stopping it",
            SystemdCommands.AlertGlyph,
            UnitAction.ResetFailed,
            registry,
            settings);
    }

    private static IPluginCommand Build(
        string commandName,
        string displayName,
        string description,
        string icon,
        UnitAction action,
        UnitRegistry registry,
        SystemdSettings settings)
    {
        CommandDescriptor descriptor = SystemdCommands.UnitDescriptor(commandName, displayName, description, icon);
        return new UnitActionCommand(descriptor, action, registry, settings);
    }
}
