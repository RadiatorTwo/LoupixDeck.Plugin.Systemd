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

    /// <summary>
    /// The category card's glyph, taken from the host's symbol library. Commands themselves declare
    /// no icon: the host turns a command's icon into a symbol layer on the button it is dropped on,
    /// and these buttons carry their own text instead.
    /// </summary>
    public const string CogGlyph = "\U000F0493";

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
