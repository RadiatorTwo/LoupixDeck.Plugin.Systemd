using System.Globalization;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Systemd;

/// <summary>
/// A touch button that shows one piece of information about a unit and runs the folder action when
/// it is pressed. <see cref="GetText"/> is polled on a timer and must not wait for D-Bus, so every
/// value comes from the registry's cached snapshot.
/// </summary>
internal sealed class UnitDisplayCommand(
    CommandDescriptor descriptor,
    Func<UnitState, IPluginHost, string> format,
    UnitRegistry registry,
    SystemdSettings settings) : UnitCommandBase(descriptor, registry, settings), IDisplayCommand
{
    public override ButtonTargets SupportedTargets => ButtonTargets.TouchButton;

    /// <summary>
    /// State changes arrive through signals; the poll only keeps the uptime moving and covers the
    /// short window before the first read comes back.
    /// </summary>
    public TimeSpan UpdateInterval => TimeSpan.FromSeconds(1);

    public string GetText(CommandContext ctx)
    {
        UnitId id = ResolveUnit(ctx);

        if (!id.IsValid)
        {
            return ctx.Host.Tr("No unit");
        }

        return format(Registry.Get(id), ctx.Host);
    }

    protected override Task ExecuteCore(CommandContext ctx)
    {
        UnitAction? action = Settings.FolderAction;

        if (action is null)
        {
            return Task.CompletedTask;
        }

        return RunActionAsync(ctx, ResolveUnit(ctx), action.Value);
    }
}

/// <summary>Builds the display commands the plugin publishes.</summary>
internal static class SystemdDisplayCommands
{
    public static IEnumerable<IPluginCommand> Create(UnitRegistry registry, SystemdSettings settings)
    {
        yield return Build(
            SystemdCommands.Prefix + "UnitStatus",
            "Unit Status",
            "Show the unit name and what it is doing",
            FormatStatus,
            registry,
            settings);

        yield return Build(
            SystemdCommands.Prefix + "UnitName",
            "Unit Name",
            "Show the name of the unit",
            (state, _) => state.ShortName,
            registry,
            settings);

        yield return Build(
            SystemdCommands.Prefix + "UnitDescription",
            "Unit Description",
            "Show the description systemd has for the unit",
            (state, host) => state.Description.Length > 0 ? state.Description : host.Tr(SystemdCommands.StateText(state)),
            registry,
            settings);

        yield return Build(
            SystemdCommands.Prefix + "UnitActiveState",
            "Unit State",
            "Show whether the unit is running, stopped or failed",
            (state, host) => $"{SystemdCommands.StateGlyph(state)} {host.Tr(SystemdCommands.StateText(state))}",
            registry,
            settings);

        yield return Build(
            SystemdCommands.Prefix + "UnitSubState",
            "Unit Sub State",
            "Show the detailed state systemd reports, for example running or dead",
            (state, host) => state.SubState.Length > 0 ? state.SubState : host.Tr("Unknown"),
            registry,
            settings);

        yield return Build(
            SystemdCommands.Prefix + "UnitLoadState",
            "Unit Load State",
            "Show whether systemd could load the unit",
            (state, host) => state.LoadState.Length > 0 ? state.LoadState : host.Tr("Unknown"),
            registry,
            settings);

        yield return Build(
            SystemdCommands.Prefix + "UnitFileState",
            "Unit File State",
            "Show whether the unit starts on its own, for example enabled or disabled",
            (state, host) => state.UnitFileState.Length > 0 ? state.UnitFileState : host.Tr("Unknown"),
            registry,
            settings);

        yield return Build(
            SystemdCommands.Prefix + "UnitUptime",
            "Unit Uptime",
            "Show how long the unit has been running",
            FormatUptime,
            registry,
            settings);

        yield return Build(
            SystemdCommands.Prefix + "UnitMainPid",
            "Unit Main Process",
            "Show the process id of the unit's main process",
            (state, host) => state.MainPid > 0
                ? state.MainPid.ToString(CultureInfo.InvariantCulture)
                : host.Tr("No process"),
            registry,
            settings);

        yield return Build(
            SystemdCommands.Prefix + "UnitResult",
            "Unit Result",
            "Show how the unit ended the last time it ran",
            (state, host) => state.Result.Length > 0 ? state.Result : host.Tr("Unknown"),
            registry,
            settings);

        yield return Build(
            SystemdCommands.Prefix + "UnitDomain",
            "Unit Instance",
            "Show whether the unit belongs to the user or the system instance",
            (state, host) => host.Tr(UnitDomainParser.ToEnglishText(state.Id.Domain)),
            registry,
            settings);
    }

    /// <summary>The composite button from the issue: unit name on top, state below it.</summary>
    private static string FormatStatus(UnitState state, IPluginHost host)
    {
        string status = $"{SystemdCommands.StateGlyph(state)} {host.Tr(SystemdCommands.StateText(state))}";
        return string.Join(Environment.NewLine, state.ShortName, status);
    }

    private static string FormatUptime(UnitState state, IPluginHost host)
    {
        if (state.ActiveEnter is not { } since || !state.IsActive)
        {
            return host.Tr(SystemdCommands.StateText(state));
        }

        TimeSpan uptime = DateTimeOffset.UtcNow - since;

        if (uptime < TimeSpan.Zero)
        {
            uptime = TimeSpan.Zero;
        }

        if (uptime.TotalDays >= 1)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}d {1}h", (int)uptime.TotalDays, uptime.Hours);
        }

        if (uptime.TotalHours >= 1)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}h {1}m", (int)uptime.TotalHours, uptime.Minutes);
        }

        return string.Format(CultureInfo.InvariantCulture, "{0}m {1}s", (int)uptime.TotalMinutes, uptime.Seconds);
    }

    private static IPluginCommand Build(
        string commandName,
        string displayName,
        string description,
        Func<UnitState, IPluginHost, string> format,
        UnitRegistry registry,
        SystemdSettings settings)
    {
        CommandDescriptor descriptor = SystemdCommands.UnitDescriptor(commandName, displayName, description);
        return new UnitDisplayCommand(descriptor, format, registry, settings);
    }
}
