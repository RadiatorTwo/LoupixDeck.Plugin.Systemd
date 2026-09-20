namespace LoupixDeck.Plugin.Systemd;

/// <summary>The runtime actions this version offers. Nothing here changes a unit file.</summary>
public enum UnitAction
{
    Start,
    Stop,
    Restart,
    Reload,

    /// <summary>Stops a running unit and starts a stopped one.</summary>
    Toggle,

    /// <summary>Clears the failed state without touching the unit itself.</summary>
    ResetFailed
}

/// <summary>How a requested action ended, once its systemd job finished.</summary>
/// <param name="Outcome">The classification a button reacts to.</param>
/// <param name="JobResult">systemd's own result string, or empty when no job was created.</param>
public readonly record struct UnitActionResult(UnitCallOutcome Outcome, string JobResult)
{
    public bool IsSuccess => Outcome == UnitCallOutcome.Ok;
}

/// <summary>Parses the action token stored in the settings.</summary>
internal static class UnitActionParser
{
    public const string ToggleValue = "toggle";
    public const string StartValue = "start";
    public const string StopValue = "stop";
    public const string RestartValue = "restart";
    public const string ReloadValue = "reload";
    public const string StatusValue = "status";

    /// <summary>Every token the settings accept, for the field's help text.</summary>
    public const string SupportedValues = "toggle, start, stop, restart, reload, status";

    /// <summary>
    /// Parses a stored token. <c>status</c> means "do nothing, only show the unit" and is
    /// returned as null, so the caller can tell it apart from a real action.
    /// </summary>
    public static UnitAction? Parse(string? value, UnitAction? fallback)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            StartValue => UnitAction.Start,
            StopValue => UnitAction.Stop,
            RestartValue => UnitAction.Restart,
            ReloadValue => UnitAction.Reload,
            ToggleValue => UnitAction.Toggle,
            StatusValue => null,
            _ => fallback
        };
    }

    /// <summary>The English label of an action, used for command names and log text.</summary>
    public static string ToEnglishText(UnitAction action)
    {
        return action switch
        {
            UnitAction.Start => "Start",
            UnitAction.Stop => "Stop",
            UnitAction.Restart => "Restart",
            UnitAction.Reload => "Reload",
            UnitAction.Toggle => "Toggle",
            _ => "Reset Failed"
        };
    }
}
