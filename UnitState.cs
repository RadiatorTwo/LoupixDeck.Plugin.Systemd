namespace LoupixDeck.Plugin.Systemd;

/// <summary>Whether the plugin currently has usable information about a unit.</summary>
public enum UnitAvailability
{
    /// <summary>Nothing has been read yet.</summary>
    Unknown,

    /// <summary>The state below was read from systemd.</summary>
    Known,

    /// <summary>systemd does not know this unit.</summary>
    NotFound,

    /// <summary>Reading the unit was refused.</summary>
    PermissionDenied,

    /// <summary>The instance is not reachable right now.</summary>
    Unavailable
}

/// <summary>
/// Everything the plugin caches about one unit. It is a snapshot: the registry replaces the whole
/// record instead of mutating it, so a display command always reads a consistent set of values.
/// </summary>
public sealed record UnitState
{
    public required UnitId Id { get; init; }

    /// <summary>The object path of the unit, or empty while it is unknown.</summary>
    public string ObjectPath { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    /// <summary>loaded, not-found, bad-setting, error or masked.</summary>
    public string LoadState { get; init; } = string.Empty;

    /// <summary>active, reloading, inactive, failed, activating or deactivating.</summary>
    public string ActiveState { get; init; } = string.Empty;

    /// <summary>The unit-type specific sub state, for example running or dead.</summary>
    public string SubState { get; init; } = string.Empty;

    /// <summary>enabled, disabled, static, masked and so on; empty when the unit has no unit file.</summary>
    public string UnitFileState { get; init; } = string.Empty;

    /// <summary>When the unit became active, or null while it is not active.</summary>
    public DateTimeOffset? ActiveEnter { get; init; }

    /// <summary>The main process of a service, or 0.</summary>
    public uint MainPid { get; init; }

    /// <summary>The result of the last run of a service, for example success or exit-code.</summary>
    public string Result { get; init; } = string.Empty;

    /// <summary>When a timer elapses next, or null for any other unit and for a timer with nothing scheduled.</summary>
    public DateTimeOffset? NextElapse { get; init; }

    /// <summary>When a timer last elapsed, or null when it never did.</summary>
    public DateTimeOffset? LastTrigger { get; init; }

    /// <summary>The unit a timer starts, usually the service of the same name; empty for other units.</summary>
    public string TriggerUnit { get; init; } = string.Empty;

    public bool CanStart { get; init; }

    public bool CanStop { get; init; }

    public bool CanReload { get; init; }

    public UnitAvailability Availability { get; init; } = UnitAvailability.Unknown;

    /// <summary>
    /// The outcome of the last command the user ran on this unit. A denial is kept here instead of
    /// in <see cref="ActiveState"/>, so a unit that keeps running is never shown as failed only
    /// because the user was not allowed to stop it.
    /// </summary>
    public UnitCallOutcome LastOutcome { get; init; } = UnitCallOutcome.Ok;

    public DateTimeOffset LastUpdate { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>The unit name as a button shows it; see <see cref="UnitTypeParser.ShortName"/>.</summary>
    public string ShortName => UnitTypeParser.ShortName(Id.Name);

    /// <summary>True while systemd reports the unit as running or reloading.</summary>
    public bool IsActive =>
        ActiveState is "active" or "reloading";

    /// <summary>True when systemd knows the unit but has no unit file or state for it.</summary>
    public bool IsLoaded =>
        string.Equals(LoadState, "loaded", StringComparison.Ordinal);

    /// <summary>A placeholder used until the first read for a unit comes back.</summary>
    public static UnitState Pending(UnitId id) => new()
    {
        Id = id,
        Availability = UnitAvailability.Unknown
    };
}
