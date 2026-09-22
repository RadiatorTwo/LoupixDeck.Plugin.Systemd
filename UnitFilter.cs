namespace LoupixDeck.Plugin.Systemd;

/// <summary>
/// Which units the selector offers. The SDK has no pick list, so the filter is a stored token the
/// command menu and the settings listing both read.
/// </summary>
public enum UnitFilter
{
    /// <summary>Everything the instance reports, subject only to the visibility toggles.</summary>
    All,

    /// <summary>Units systemd has a unit file for.</summary>
    Loaded,

    /// <summary>Units that are running, starting or reloading.</summary>
    Active,

    /// <summary>Units systemd marked as failed.</summary>
    Failed
}

/// <summary>Converts a <see cref="UnitFilter"/> to and from the token stored in the config.</summary>
internal static class UnitFilterParser
{
    public const string AllValue = "all";
    public const string LoadedValue = "loaded";
    public const string ActiveValue = "active";
    public const string FailedValue = "failed";

    /// <summary>The tokens a setting description lists.</summary>
    public const string SupportedValues = AllValue + ", " + LoadedValue + ", " + ActiveValue + ", " + FailedValue;

    /// <summary>Parses a stored token. Anything unknown falls back to the unfiltered list.</summary>
    public static UnitFilter Parse(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            LoadedValue => UnitFilter.Loaded,
            ActiveValue => UnitFilter.Active,
            FailedValue => UnitFilter.Failed,
            _ => UnitFilter.All
        };
    }

    /// <summary>The token persisted in the config. Never change these.</summary>
    public static string ToParameterValue(UnitFilter filter)
    {
        return filter switch
        {
            UnitFilter.Loaded => LoadedValue,
            UnitFilter.Active => ActiveValue,
            UnitFilter.Failed => FailedValue,
            _ => AllValue
        };
    }

    /// <summary>The English label a translated menu group is built from.</summary>
    public static string ToEnglishText(UnitFilter filter)
    {
        return filter switch
        {
            UnitFilter.Loaded => "Loaded",
            UnitFilter.Active => "Active",
            UnitFilter.Failed => "Failed",
            _ => "All"
        };
    }
}

/// <summary>
/// The rules behind the selector: which units a filter keeps and what a search term matches. A
/// search reads the description as well as the name, because a unit is usually remembered by what
/// it does rather than by the file it lives in.
/// </summary>
internal static class UnitMatch
{
    /// <summary>The groups the command menu offers below an instance, in the order they appear.</summary>
    public static readonly IReadOnlyList<UnitFilter> MenuGroups =
        [UnitFilter.Failed, UnitFilter.Active, UnitFilter.Loaded, UnitFilter.All];

    /// <summary>True when a unit belongs into that filter's list.</summary>
    public static bool MatchesFilter(UnitListEntry unit, UnitFilter filter)
    {
        return filter switch
        {
            UnitFilter.Loaded => string.Equals(unit.LoadState, "loaded", StringComparison.Ordinal),
            UnitFilter.Active => IsRunning(unit),
            UnitFilter.Failed => IsFailed(unit),
            _ => true
        };
    }

    /// <summary>True when the search term appears in the unit's name or description.</summary>
    public static bool MatchesSearch(UnitListEntry unit, string search)
    {
        if (search.Length == 0)
        {
            return true;
        }

        return unit.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
               || unit.Description.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Applies the visibility toggles, the filter and the search term in one pass, and returns the
    /// result sorted by name. A failed unit is kept even when stopped units are hidden: a unit that
    /// broke is exactly what the user is looking for.
    /// </summary>
    public static IReadOnlyList<UnitListEntry> Select(
        IEnumerable<UnitListEntry> units,
        UnitFilter filter,
        string search,
        bool showInactive,
        bool showUnloaded)
    {
        List<UnitListEntry> selected = [];

        foreach (UnitListEntry unit in units)
        {
            if (!showInactive && !IsRunning(unit) && !IsFailed(unit))
            {
                continue;
            }

            if (!showUnloaded && !string.Equals(unit.LoadState, "loaded", StringComparison.Ordinal))
            {
                continue;
            }

            if (!MatchesFilter(unit, filter) || !MatchesSearch(unit, search))
            {
                continue;
            }

            selected.Add(unit);
        }

        selected.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        return selected;
    }

    public static bool IsRunning(UnitListEntry unit) =>
        unit.ActiveState is "active" or "activating" or "reloading";

    public static bool IsFailed(UnitListEntry unit) =>
        string.Equals(unit.ActiveState, "failed", StringComparison.Ordinal);
}
