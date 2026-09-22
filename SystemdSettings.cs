using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Systemd;

/// <summary>
/// Typed access to the plugin's settings. The keys are persisted in
/// <c>plugins/systemd/settings.json</c> and must not change: a file written by an older version
/// has to keep loading unchanged.
/// </summary>
internal sealed class SystemdSettings(IPluginSettings settings)
{
    public const string PreferredDomainKey = "domain:preferred";
    public const string ShowSystemUnitsKey = "domain:showSystemUnits";
    public const string UnitTypesKey = "units:types";
    public const string FavoritesKey = "units:favorites";
    public const string ShowInactiveKey = "units:showInactive";
    public const string ShowUnloadedKey = "units:showUnloaded";
    public const string UnitFilterKey = "units:filter";
    public const string UnitSearchKey = "units:search";
    public const string DefaultActionKey = "folder:defaultAction";
    public const string DBusTimeoutKey = "behavior:dbusTimeoutMs";
    public const string CommandTimeoutKey = "behavior:commandTimeoutMs";
    public const string AllowPersistentKey = "persistent:allow";
    public const string ConfirmPersistentKey = "persistent:confirm";

    public const bool DefaultShowSystemUnits = true;
    public const bool DefaultShowInactive = true;
    public const bool DefaultShowUnloaded = false;
    public const string DefaultUnitTypes = UnitTypeParser.ServiceValue;
    public const string DefaultUnitFilter = UnitFilterParser.AllValue;
    public const string DefaultAction = UnitActionParser.ToggleValue;
    public const int DefaultDBusTimeoutMilliseconds = 2000;
    public const int DefaultCommandTimeoutMilliseconds = 10000;
    public const bool DefaultAllowPersistent = false;
    public const bool DefaultConfirmPersistent = true;

    /// <summary>How many favorites the slot commands can address.</summary>
    public const int FavoriteSlotCount = 10;

    private const int MinimumDBusTimeoutMilliseconds = 250;
    private const int MaximumDBusTimeoutMilliseconds = 10000;
    private const int MinimumCommandTimeoutMilliseconds = 1000;
    private const int MaximumCommandTimeoutMilliseconds = 120000;

    /// <summary>The instance a command without a domain prefix acts on.</summary>
    public UnitDomain PreferredDomain =>
        UnitDomainParser.Parse(settings.Get(PreferredDomainKey, UnitDomainParser.UserValue));

    /// <summary>When off, the system bus is never opened at all.</summary>
    public bool ShowSystemUnits => settings.Get(ShowSystemUnitsKey, DefaultShowSystemUnits);

    public bool ShowInactiveUnits => settings.Get(ShowInactiveKey, DefaultShowInactive);

    public bool ShowUnloadedUnits => settings.Get(ShowUnloadedKey, DefaultShowUnloaded);

    /// <summary>
    /// Which units the selector offers. An older settings file has no such key, so the absent
    /// value means the unfiltered list the plugin showed before the filter existed.
    /// </summary>
    public UnitFilter UnitFilter => UnitFilterParser.Parse(settings.Get(UnitFilterKey, DefaultUnitFilter));

    /// <summary>
    /// The term the selector narrows its list to. An empty term keeps every unit, which is what an
    /// older settings file without the key gets.
    /// </summary>
    public string UnitSearch => (settings.Get(UnitSearchKey, string.Empty) ?? string.Empty).Trim();

    /// <summary>
    /// The unit types the selector lists. An older file stores "service" or nothing at all, and
    /// both read back as services only, which is what the plugin listed before.
    /// </summary>
    public IReadOnlyList<string> UnitTypes =>
        UnitTypeParser.Parse(SystemdSettingsList.Parse(settings.Get(UnitTypesKey, DefaultUnitTypes)));

    /// <summary>The units shown in the folder and offered first in the menu.</summary>
    public IReadOnlyList<UnitId> Favorites
    {
        get
        {
            UnitDomain fallback = PreferredDomain;
            List<UnitId> favorites = [];

            foreach (string entry in SystemdSettingsList.Parse(settings.Get(FavoritesKey, string.Empty)))
            {
                UnitId id = UnitId.Parse(entry, fallback);

                if (id.IsValid && !favorites.Contains(id))
                {
                    favorites.Add(id);
                }
            }

            return favorites;
        }
    }

    /// <summary>What pressing an entry in the units folder does, or null for "only show the unit".</summary>
    public UnitAction? FolderAction =>
        UnitActionParser.Parse(settings.Get(DefaultActionKey, DefaultAction), UnitAction.Toggle);

    /// <summary>Per-call D-Bus timeout, clamped to a range that stays usable.</summary>
    public int DBusTimeoutMilliseconds =>
        ReadClamped(DBusTimeoutKey, DefaultDBusTimeoutMilliseconds, MinimumDBusTimeoutMilliseconds, MaximumDBusTimeoutMilliseconds);

    /// <summary>How long a command waits for its systemd job.</summary>
    public TimeSpan CommandTimeout => TimeSpan.FromMilliseconds(
        ReadClamped(CommandTimeoutKey, DefaultCommandTimeoutMilliseconds, MinimumCommandTimeoutMilliseconds, MaximumCommandTimeoutMilliseconds));

    /// <summary>
    /// The explicit opt-in for enable, disable, mask and unmask. Off by default and off for every
    /// file written before these actions existed, so no button can change a unit file by accident.
    /// </summary>
    public bool AllowPersistentActions => settings.Get(AllowPersistentKey, DefaultAllowPersistent);

    /// <summary>When on, a persistent action runs only on a second press shortly after the first.</summary>
    public bool ConfirmPersistentActions => settings.Get(ConfirmPersistentKey, DefaultConfirmPersistent);

    /// <summary>The favorite behind a slot command, or an invalid id when the slot is empty.</summary>
    public UnitId FavoriteAt(int slotIndex)
    {
        IReadOnlyList<UnitId> favorites = Favorites;
        return slotIndex >= 0 && slotIndex < favorites.Count ? favorites[slotIndex] : default;
    }

    /// <summary>Adds a unit to the favorites. Returns false when it was already there.</summary>
    public bool AddFavorite(UnitId id)
    {
        if (!id.IsValid)
        {
            return false;
        }

        List<UnitId> favorites = [.. Favorites];

        if (favorites.Contains(id))
        {
            return false;
        }

        favorites.Add(id);
        WriteFavorites(favorites);
        return true;
    }

    /// <summary>Removes a unit from the favorites. Returns false when it was not there.</summary>
    public bool RemoveFavorite(UnitId id)
    {
        List<UnitId> favorites = [.. Favorites];

        if (!favorites.Remove(id))
        {
            return false;
        }

        WriteFavorites(favorites);
        return true;
    }

    /// <summary>Empties the favorites list.</summary>
    public void ClearFavorites() => WriteFavorites([]);

    private void WriteFavorites(IEnumerable<UnitId> favorites)
    {
        settings.Set(FavoritesKey, SystemdSettingsList.Format(favorites.Select(favorite => favorite.ToString())));
        settings.Save();
    }

    /// <summary>
    /// Reads a number setting and keeps it inside its supported range. The host stores it as a
    /// JSON integer and falls back to the default when a stored value cannot be read as one.
    /// </summary>
    private int ReadClamped(string key, int fallback, int minimum, int maximum) =>
        (int)Math.Clamp(settings.Get(key, (long)fallback), minimum, maximum);
}
