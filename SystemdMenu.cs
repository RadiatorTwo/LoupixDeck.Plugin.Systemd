using System.Globalization;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Systemd;

/// <summary>
/// Builds the command picker tree. The SDK has no dropdown and no search field for a parameter, so
/// a unit is chosen by walking to it: instance, then a group of first letters, then the unit. Every
/// leaf bakes the unit into the command's parameter.
/// <para>
/// The host gives a menu contributor only a few seconds, so everything here comes from the cached
/// listing rather than from a fresh call.
/// </para>
/// </summary>
internal sealed class SystemdMenu(UnitRegistry registry, SystemdSettings settings)
{
    /// <summary>How many units go into one first-letter group before it is split.</summary>
    private const int GroupSize = 10;

    public async Task<IReadOnlyList<MenuNode>> BuildAsync()
    {
        List<MenuNode> children = [];

        MenuNode? favorites = BuildFavorites();

        if (favorites is not null)
        {
            children.Add(favorites);
        }

        MenuNode? slots = BuildSlots();

        if (slots is not null)
        {
            children.Add(slots);
        }

        foreach (UnitDomain domain in registry.Domains)
        {
            MenuNode? units = await BuildDomainAsync(domain).ConfigureAwait(false);

            if (units is not null)
            {
                children.Add(units);
            }
        }

        children.Add(new MenuNode { Name = "Open Units Folder", CommandName = SystemdCommands.OpenUnits });

        return [new MenuNode { Name = SystemdCommands.Group, CommandName = string.Empty, Children = children }];
    }

    private MenuNode? BuildFavorites()
    {
        IReadOnlyList<UnitId> favorites = settings.Favorites;

        if (favorites.Count == 0)
        {
            return null;
        }

        List<MenuNode> children = [];

        foreach (UnitId id in favorites)
        {
            children.Add(BuildUnitNode(id, registry.Get(id).Description));
        }

        return new MenuNode { Name = "Favorites", CommandName = string.Empty, Children = children };
    }

    /// <summary>
    /// The slot commands. They are the only buttons that follow a unit's state, so the label says
    /// which favorite a slot currently shows.
    /// </summary>
    private MenuNode? BuildSlots()
    {
        IReadOnlyList<UnitId> favorites = settings.Favorites;

        if (favorites.Count == 0)
        {
            return null;
        }

        List<MenuNode> children = [];

        for (int index = 0; index < favorites.Count && index < SystemdSettings.FavoriteSlotCount; index++)
        {
            string number = (index + 1).ToString("00", CultureInfo.InvariantCulture);

            children.Add(new MenuNode
            {
                Name = $"{number} — {favorites[index].Name}",
                CommandName = SystemdCommands.Prefix + "Favorite" + number
            });
        }

        return new MenuNode { Name = "Favorite Slots", CommandName = string.Empty, Children = children };
    }

    private async Task<MenuNode?> BuildDomainAsync(UnitDomain domain)
    {
        IReadOnlyList<UnitListEntry> units = await registry.ListServicesAsync(domain).ConfigureAwait(false);
        List<UnitListEntry> visible = [];

        foreach (UnitListEntry unit in units)
        {
            if (!settings.ShowInactiveUnits && !IsRunning(unit))
            {
                continue;
            }

            if (!settings.ShowUnloadedUnits && !string.Equals(unit.LoadState, "loaded", StringComparison.Ordinal))
            {
                continue;
            }

            visible.Add(unit);
        }

        if (visible.Count == 0)
        {
            return null;
        }

        visible.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));

        List<MenuNode> groups = [];

        for (int start = 0; start < visible.Count; start += GroupSize)
        {
            int length = Math.Min(GroupSize, visible.Count - start);
            List<MenuNode> children = [];

            for (int index = start; index < start + length; index++)
            {
                UnitListEntry unit = visible[index];
                children.Add(BuildUnitNode(new UnitId(domain, unit.Name), unit.Description));
            }

            char first = GroupLetter(visible[start].Name);
            char last = GroupLetter(visible[start + length - 1].Name);

            groups.Add(new MenuNode
            {
                Name = first == last ? first.ToString() : $"{first} – {last}",
                CommandName = string.Empty,
                Children = children
            });
        }

        string name = domain == UnitDomain.System ? "System Units" : "User Units";
        return new MenuNode { Name = name, CommandName = string.Empty, Children = groups };
    }

    /// <summary>The actions offered for one unit, each with the unit baked into its parameter.</summary>
    private static MenuNode BuildUnitNode(UnitId id, string description)
    {
        Dictionary<string, string> parameters = new(StringComparer.Ordinal)
        {
            [SystemdCommands.UnitParameter] = id.ToString()
        };

        // The status button carries a second value, the switch that prints the unit name.
        Dictionary<string, string> statusParameters = new(StringComparer.Ordinal)
        {
            [SystemdCommands.UnitParameter] = id.ToString(),
            [SystemdCommands.ShowNameParameter] = SystemdCommands.SwitchOn
        };

        List<MenuNode> actions =
        [
            new() { Name = "Toggle Unit", CommandName = SystemdCommands.Toggle, Parameters = parameters },
            new() { Name = "Start Unit", CommandName = SystemdCommands.Start, Parameters = parameters },
            new() { Name = "Stop Unit", CommandName = SystemdCommands.Stop, Parameters = parameters },
            new() { Name = "Restart Unit", CommandName = SystemdCommands.Restart, Parameters = parameters },
            new() { Name = "Reload Unit", CommandName = SystemdCommands.Reload, Parameters = parameters },
            new() { Name = "Reset Failed Unit", CommandName = SystemdCommands.ResetFailed, Parameters = parameters },
            new() { Name = "Unit Status", CommandName = SystemdCommands.Prefix + "UnitStatus", Parameters = statusParameters },
            new() { Name = "Unit Uptime", CommandName = SystemdCommands.Prefix + "UnitUptime", Parameters = parameters },
            new() { Name = "Add Unit to Favorites", CommandName = SystemdCommands.AddFavorite, Parameters = parameters },
            new() { Name = "Remove Unit from Favorites", CommandName = SystemdCommands.RemoveFavorite, Parameters = parameters }
        ];

        return new MenuNode
        {
            // MenuNode carries no icon, so the description is what tells two similar units apart.
            Name = description.Length > 0 ? $"{id.Name} — {description}" : id.Name,
            CommandName = string.Empty,
            Children = actions
        };
    }

    private static bool IsRunning(UnitListEntry unit) =>
        unit.ActiveState is "active" or "activating" or "reloading";

    /// <summary>
    /// The letter a group is named after. A unit name can start with anything, and systemd escapes
    /// characters inside it, so only a plain letter or digit is used as a label.
    /// </summary>
    private static char GroupLetter(string unitName)
    {
        char first = unitName.Length > 0 ? char.ToUpperInvariant(unitName[0]) : '#';
        return char.IsAsciiLetterOrDigit(first) ? first : '#';
    }
}
