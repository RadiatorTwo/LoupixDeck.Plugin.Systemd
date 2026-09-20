using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Systemd;

/// <summary>
/// The names and glyphs the plugin publishes. A command name is a stable public identifier: a
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
    /// The glyph the command picker shows, mdi-cog-outline.
    /// <para>
    /// It sits deliberately outside the host's curated symbol set. Dropping a command on a touch
    /// button turns the glyph the picker row shows into a symbol layer, and a row without an icon
    /// of its own inherits one from its category — so leaving the icon empty produced a folder
    /// symbol rather than none. A glyph the symbol set does not know resolves to no symbol at all,
    /// which leaves the button with the caption these buttons want.
    /// </para>
    /// </summary>
    public const string PickerGlyph = "\U000F08BB";

    /// <summary>Builds the descriptor of a command that acts on the unit in its parameter.</summary>
    public static CommandDescriptor UnitDescriptor(
        string commandName,
        string displayName,
        string description,
        bool hiddenFromMenu = false) => new()
    {
        CommandName = commandName,
        DisplayName = displayName,
        Group = Group,
        Icon = PickerGlyph,
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
