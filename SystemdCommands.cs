using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Systemd;

/// <summary>
/// The names the plugin publishes. A command name is a stable public identifier: a
/// button stores it, so renaming one would break every deck that uses it.
/// </summary>
internal static class SystemdCommands
{
    public const string Prefix = "Systemd.";
    public const string Group = "Systemd";

    public const string Start = Prefix + "Start";
    public const string Stop = Prefix + "Stop";
    public const string Restart = Prefix + "Restart";
    public const string Reload = Prefix + "Reload";
    public const string Toggle = Prefix + "Toggle";
    public const string ResetFailed = Prefix + "ResetFailed";
    public const string Enable = Prefix + "Enable";
    public const string Disable = Prefix + "Disable";
    public const string Mask = Prefix + "Mask";
    public const string Unmask = Prefix + "Unmask";
    public const string OpenUnits = Prefix + "OpenUnits";
    public const string AddFavorite = Prefix + "AddFavorite";
    public const string RemoveFavorite = Prefix + "RemoveFavorite";

    /// <summary>The name of the unit parameter every unit command carries.</summary>
    public const string UnitParameter = "unit";

    /// <summary>The placeholder the command builder shows for that parameter.</summary>
    public const string UnitTemplate = "({unit})";

    /// <summary>The name of the switch that decides whether the status button prints the unit name.</summary>
    public const string ShowNameParameter = "showName";

    /// <summary>The template of the status command, which carries the unit and that switch.</summary>
    public const string StatusTemplate = "({unit},{showName})";

    /// <summary>The value the switch carries when the name is printed.</summary>
    public const string SwitchOn = "1";

    /// <summary>The value the switch carries when the name is left out.</summary>
    public const string SwitchOff = "0";

    /// <summary>
    /// Reads a switch parameter. Everything but an explicit off value counts as on, so a button
    /// saved before the switch existed, or with an empty parameter, keeps printing the name.
    /// </summary>
    public static bool ReadSwitch(CommandContext ctx, int index)
    {
        string? value = ctx.Parameters.Length > index ? ctx.Parameters[index]?.Trim() : null;

        return value?.ToLowerInvariant() switch
        {
            SwitchOff or "false" or "no" or "off" => false,
            _ => true
        };
    }

    /// <summary>
    /// Builds the descriptor of a command that acts on the unit in its parameter. Its icon shows in
    /// the picker and on the button; the caption is left to the host, because a menu leaf carries
    /// the unit name there and a button must say which unit it acts on.
    /// </summary>
    public static CommandDescriptor UnitDescriptor(
        string commandName,
        string displayName,
        string description,
        string glyph,
        bool hiddenFromMenu = false,
        string? color = null) => new()
    {
        CommandName = commandName,
        DisplayName = displayName,
        Group = Group,
        Icon = glyph,
        ButtonLayout = SystemdButtonLayouts.IconWithCaption(glyph, caption: null, tall: true, color),
        Description = description,
        ParameterTemplate = UnitTemplate,
        Parameters = [new CommandParameter(UnitParameter, typeof(string))],
        HiddenFromMenu = hiddenFromMenu
    };

    /// <summary>The glyph shown in front of a unit state on a button or in the menu.</summary>
    public static string StateGlyph(UnitState state)
    {
        if (state.Availability == UnitAvailability.NotFound)
        {
            return "?";
        }

        if (state.LastOutcome == UnitCallOutcome.PermissionDenied)
        {
            return "!";
        }

        return state.ActiveState switch
        {
            "active" => "●",
            "reloading" => "◐",
            "activating" or "deactivating" => "◌",
            "failed" => "⚠",
            _ => "○"
        };
    }

    /// <summary>The English state text a button shows, which the host translates.</summary>
    public static string StateText(UnitState state)
    {
        if (state.Availability == UnitAvailability.NotFound)
        {
            return "Not found";
        }

        if (state.Availability == UnitAvailability.Unavailable)
        {
            return "Unavailable";
        }

        if (state.LastOutcome == UnitCallOutcome.PermissionDenied)
        {
            return "No permission";
        }

        return state.ActiveState switch
        {
            "active" => "Running",
            "reloading" => "Reloading",
            "activating" => "Starting",
            "deactivating" => "Stopping",
            "failed" => "Failed",
            "inactive" => "Stopped",
            "" => "Unknown",
            _ => state.ActiveState
        };
    }
}
