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
    Func<UnitState, CommandContext, string> format,
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

        return format(Registry.Get(id), ctx);
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
        CommandDescriptor status = new()
        {
            CommandName = SystemdCommands.Prefix + "UnitStatus",
            DisplayName = "Unit Status",
            Group = SystemdCommands.Group,
            Description = "Show what the unit is doing, with its name above the state. Set the second value to 0 to leave the name out.",
            ParameterTemplate = SystemdCommands.StatusTemplate,
            Parameters =
            [
                new CommandParameter(SystemdCommands.UnitParameter, typeof(string)),
                new CommandParameter(SystemdCommands.ShowNameParameter, typeof(string))
                {
                    DefaultValue = SystemdCommands.SwitchOn
                }
            ]
        };

        yield return new UnitDisplayCommand(status, FormatStatus, registry, settings);

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
            (state, ctx) => state.Description.Length > 0 ? state.Description : ctx.Host.Tr(SystemdCommands.StateText(state)),
            registry,
            settings);

        yield return Build(
            SystemdCommands.Prefix + "UnitSubState",
            "Unit Sub State",
            "Show the detailed state systemd reports, for example running or dead",
            (state, ctx) => state.SubState.Length > 0 ? state.SubState : ctx.Host.Tr("Unknown"),
            registry,
            settings);

        yield return Build(
            SystemdCommands.Prefix + "UnitLoadState",
            "Unit Load State",
            "Show whether systemd could load the unit",
            (state, ctx) => state.LoadState.Length > 0 ? state.LoadState : ctx.Host.Tr("Unknown"),
            registry,
            settings);

        yield return Build(
            SystemdCommands.Prefix + "UnitFileState",
            "Unit File State",
            "Show whether the unit starts on its own, for example enabled or disabled",
            (state, ctx) => state.UnitFileState.Length > 0 ? state.UnitFileState : ctx.Host.Tr("Unknown"),
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
            (state, ctx) => state.MainPid > 0
                ? state.MainPid.ToString(CultureInfo.InvariantCulture)
                : ctx.Host.Tr("No process"),
            registry,
            settings);

        yield return Build(
            SystemdCommands.Prefix + "UnitResult",
            "Unit Result",
            "Show how the unit ended the last time it ran",
            (state, ctx) => state.Result.Length > 0 ? state.Result : ctx.Host.Tr("Unknown"),
            registry,
            settings);

        yield return Build(
            SystemdCommands.Prefix + "UnitDomain",
            "Unit Instance",
            "Show whether the unit belongs to the user or the system instance",
            (state, ctx) => ctx.Host.Tr(UnitDomainParser.ToEnglishText(state.Id.Domain)),
            registry,
            settings);
    }

    /// <summary>
    /// The state button: the glyph and the state, with the unit name above it unless the button's
    /// switch turns that off. One command covers both looks, so the menu has no second,
    /// near-identical entry.
    /// </summary>
    private static string FormatStatus(UnitState state, CommandContext ctx)
    {
        string status = $"{SystemdCommands.StateGlyph(state)} {ctx.Host.Tr(SystemdCommands.StateText(state))}";

        return SystemdCommands.ReadSwitch(ctx, 1)
            ? string.Join(Environment.NewLine, state.ShortName, status)
            : status;
    }

    private static string FormatUptime(UnitState state, CommandContext ctx)
    {
        if (state.ActiveEnter is not { } since || !state.IsActive)
        {
            return ctx.Host.Tr(SystemdCommands.StateText(state));
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
        Func<UnitState, CommandContext, string> format,
        UnitRegistry registry,
        SystemdSettings settings)
    {
        CommandDescriptor descriptor = SystemdCommands.UnitDescriptor(commandName, displayName, description);
        return new UnitDisplayCommand(descriptor, format, registry, settings);
    }
}
