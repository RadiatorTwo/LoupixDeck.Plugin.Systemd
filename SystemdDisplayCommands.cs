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
            Icon = SystemdCommands.PickerGlyph,
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
            SystemdCommands.Prefix + "TimerNextRun",
            "Timer Next Run",
            "Show how long until a timer starts its unit next",
            FormatNextRun,
            registry,
            settings);

        yield return Build(
            SystemdCommands.Prefix + "TimerLastRun",
            "Timer Last Run",
            "Show how long ago a timer last started its unit",
            FormatLastRun,
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

        return FormatSpan(DateTimeOffset.UtcNow - since);
    }

    private static string FormatNextRun(UnitState state, CommandContext ctx)
    {
        if (!TimerUnits.IsTimer(state.Id))
        {
            return ctx.Host.Tr("Not a timer");
        }

        if (state.NextElapse is not { } next)
        {
            return state.Availability == UnitAvailability.Known
                ? ctx.Host.Tr("Not scheduled")
                : ctx.Host.Tr(SystemdCommands.StateText(state));
        }

        // A template rather than two words, so a language can put the duration where it belongs.
        return string.Format(CultureInfo.InvariantCulture, ctx.Host.Tr("in {0}"), FormatSpan(next - DateTimeOffset.UtcNow));
    }

    private static string FormatLastRun(UnitState state, CommandContext ctx)
    {
        if (!TimerUnits.IsTimer(state.Id))
        {
            return ctx.Host.Tr("Not a timer");
        }

        if (state.LastTrigger is not { } last)
        {
            return state.Availability == UnitAvailability.Known
                ? ctx.Host.Tr("Never")
                : ctx.Host.Tr(SystemdCommands.StateText(state));
        }

        return string.Format(CultureInfo.InvariantCulture, ctx.Host.Tr("{0} ago"), FormatSpan(DateTimeOffset.UtcNow - last));
    }

    /// <summary>A duration in the two largest units that fit on a button, for example "2h 5m".</summary>
    private static string FormatSpan(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        if (span.TotalDays >= 1)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}d {1}h", (int)span.TotalDays, span.Hours);
        }

        if (span.TotalHours >= 1)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}h {1}m", (int)span.TotalHours, span.Minutes);
        }

        return string.Format(CultureInfo.InvariantCulture, "{0}m {1}s", (int)span.TotalMinutes, span.Seconds);
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
