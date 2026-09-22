using System.Text;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Systemd;

/// <summary>
/// The plugin's settings form. The host renders the descriptors, so only the values are declared
/// here. Labels and descriptions are English keys the host translates.
/// <para>
/// The SDK has no pick list and no search field, so the choices are text tokens and the unit list
/// is offered through the "List units" button instead. The search term and the filter are ordinary
/// settings, which is what makes that listing usable on a machine with hundreds of units.
/// </para>
/// </summary>
internal sealed class SystemdSettingsPage(SystemdSettings settings, UnitRegistry registry, IPluginHost host)
{
    /// <summary>How many units the listing button prints before it stops.</summary>
    private const int ListLimit = 40;

    public IReadOnlyList<PluginSettingDescriptor> BuildSchema() =>
    [
        new PluginSettingDescriptor
        {
            Key = "heading:domain",
            Label = "Instances",
            Kind = PluginSettingKind.Heading
        },
        new PluginSettingDescriptor
        {
            Key = SystemdSettings.PreferredDomainKey,
            Label = "Preferred instance",
            Kind = PluginSettingKind.Text,
            Description = "The instance a button acts on when its unit carries no prefix: user or system.",
            DefaultValue = UnitDomainParser.UserValue
        },
        new PluginSettingDescriptor
        {
            Key = SystemdSettings.ShowSystemUnitsKey,
            Label = "Show system units",
            Kind = PluginSettingKind.Toggle,
            Description = "Off keeps the plugin on the user instance and never opens the system bus. System units usually need an authorization the deck cannot ask for.",
            DefaultValue = SystemdSettings.DefaultShowSystemUnits
        },
        new PluginSettingDescriptor
        {
            Key = "heading:units",
            Label = "Units",
            Kind = PluginSettingKind.Heading
        },
        new PluginSettingDescriptor
        {
            Key = SystemdSettings.FavoritesKey,
            Label = "Favorite units",
            Kind = PluginSettingKind.Text,
            Description = "The units in the units folder, separated by commas, for example user:pipewire.service, system:sshd.service. The command menu fills this in for you.",
            DefaultValue = string.Empty
        },
        new PluginSettingDescriptor
        {
            Key = SystemdSettings.UnitTypesKey,
            Label = "Unit types",
            Kind = PluginSettingKind.Text,
            Description = "The unit types offered in the command menu, separated by commas: " + UnitTypeParser.SupportedValues + ".",
            DefaultValue = SystemdSettings.DefaultUnitTypes
        },
        new PluginSettingDescriptor
        {
            Key = SystemdSettings.ShowInactiveKey,
            Label = "Show stopped units",
            Kind = PluginSettingKind.Toggle,
            Description = "List units that are not running in the command menu.",
            DefaultValue = SystemdSettings.DefaultShowInactive
        },
        new PluginSettingDescriptor
        {
            Key = SystemdSettings.ShowUnloadedKey,
            Label = "Show unloaded units",
            Kind = PluginSettingKind.Toggle,
            Description = "List units systemd has no unit file for. Off keeps the menu short.",
            DefaultValue = SystemdSettings.DefaultShowUnloaded
        },
        new PluginSettingDescriptor
        {
            Key = SystemdSettings.UnitSearchKey,
            Label = "Unit search",
            Kind = PluginSettingKind.Text,
            Description = "Narrows the command menu and the listing below to the units whose name or description contains this text. Empty shows every unit.",
            DefaultValue = string.Empty
        },
        new PluginSettingDescriptor
        {
            Key = SystemdSettings.UnitFilterKey,
            Label = "Unit filter",
            Kind = PluginSettingKind.Text,
            Description = "Which units the listing below offers: " + UnitFilterParser.SupportedValues + ". The command menu offers all four as its own groups.",
            DefaultValue = SystemdSettings.DefaultUnitFilter
        },
        new PluginSettingDescriptor
        {
            Key = "heading:folder",
            Label = "Units folder",
            Kind = PluginSettingKind.Heading
        },
        new PluginSettingDescriptor
        {
            Key = SystemdSettings.DefaultActionKey,
            Label = "Action when an entry is pressed",
            Kind = PluginSettingKind.Text,
            Description = "One of " + UnitActionParser.SupportedValues + ". Use status to only show the unit without acting on it.",
            DefaultValue = SystemdSettings.DefaultAction
        },
        new PluginSettingDescriptor
        {
            Key = "heading:behavior",
            Label = "Behaviour",
            Kind = PluginSettingKind.Heading
        },
        new PluginSettingDescriptor
        {
            Key = SystemdSettings.DBusTimeoutKey,
            Label = "D-Bus timeout (ms)",
            Kind = PluginSettingKind.Number,
            Description = "How long a single call to systemd may take before it is given up (250 to 10000).",
            DefaultValue = SystemdSettings.DefaultDBusTimeoutMilliseconds
        },
        new PluginSettingDescriptor
        {
            Key = SystemdSettings.CommandTimeoutKey,
            Label = "Command timeout (ms)",
            Kind = PluginSettingKind.Number,
            Description = "How long a button waits for the unit to finish starting or stopping (1000 to 120000). The unit keeps going when the wait runs out.",
            DefaultValue = SystemdSettings.DefaultCommandTimeoutMilliseconds
        },
        new PluginSettingDescriptor
        {
            Key = "heading:persistent",
            Label = "Persistent actions",
            Kind = PluginSettingKind.Heading
        },
        new PluginSettingDescriptor
        {
            Key = SystemdSettings.AllowPersistentKey,
            Label = "Allow persistent actions",
            Kind = PluginSettingKind.Toggle,
            Description = "Lets buttons enable, disable, mask and unmask units. These change the unit file state and survive a reboot, unlike start and stop. Off, those buttons do nothing.",
            DefaultValue = SystemdSettings.DefaultAllowPersistent
        },
        new PluginSettingDescriptor
        {
            Key = SystemdSettings.ConfirmPersistentKey,
            Label = "Confirm with a second press",
            Kind = PluginSettingKind.Toggle,
            Description = "A persistent action runs only when the button is pressed again within three seconds.",
            DefaultValue = SystemdSettings.DefaultConfirmPersistent
        }
    ];

    public IReadOnlyList<PluginSettingAction> BuildActions() =>
    [
        new PluginSettingAction
        {
            Label = "List units",
            Invoke = ListUnitsAsync
        },
        new PluginSettingAction
        {
            Label = "Add listed units to favorites",
            Invoke = AddListedUnitsAsync
        },
        new PluginSettingAction
        {
            Label = "Clear favorite units",
            Invoke = ClearFavoritesAsync
        }
    ];

    /// <summary>
    /// Prints the units of every served instance that pass the search and the filter. This is what
    /// the missing search field is replaced with: the names can be copied straight into the
    /// favorites field, or added to them with the button next to this one.
    /// </summary>
    private async Task<string> ListUnitsAsync()
    {
        StringBuilder builder = new();
        int printed = 0;

        foreach (UnitDomain domain in registry.Domains)
        {
            if (!registry.IsAvailable(domain))
            {
                builder.AppendLine($"{UnitDomainParser.ToEnglishText(domain)}: {host.Tr("Unavailable")}");
                continue;
            }

            IReadOnlyList<UnitListEntry> units = await SelectAsync(domain).ConfigureAwait(false);
            builder.AppendLine($"{UnitDomainParser.ToEnglishText(domain)}: {units.Count}");

            foreach (UnitListEntry unit in units)
            {
                if (printed >= ListLimit)
                {
                    builder.AppendLine("…");
                    return builder.ToString();
                }

                string description = unit.Description.Length > 0 ? $" — {unit.Description}" : string.Empty;
                builder.AppendLine($"{UnitDomainParser.ToParameterValue(domain)}:{unit.Name} — {unit.ActiveState}{description}");
                printed++;
            }
        }

        return builder.Length == 0 ? host.Tr("No units") : builder.ToString();
    }

    /// <summary>
    /// Adds every unit the listing shows to the favorites. A search that narrows the list to a
    /// handful is the closest the settings page gets to picking units from a selector, so the same
    /// search decides what is added.
    /// </summary>
    private async Task<string> AddListedUnitsAsync()
    {
        int added = 0;
        int listed = 0;

        foreach (UnitDomain domain in registry.Domains)
        {
            if (!registry.IsAvailable(domain))
            {
                continue;
            }

            foreach (UnitListEntry unit in await SelectAsync(domain).ConfigureAwait(false))
            {
                if (listed >= ListLimit)
                {
                    break;
                }

                listed++;

                if (settings.AddFavorite(new UnitId(domain, unit.Name)))
                {
                    added++;
                }
            }
        }

        if (listed == 0)
        {
            return host.Tr("No units");
        }

        return added == 0
            ? host.Tr("Already a favorite")
            : $"{host.Tr("Added to favorites")}: {added}";
    }

    /// <summary>The units of one instance, seen through the search term and the filter.</summary>
    private async Task<IReadOnlyList<UnitListEntry>> SelectAsync(UnitDomain domain)
    {
        IReadOnlyList<UnitListEntry> units = await registry.ListUnitsAsync(domain, settings.UnitTypes).ConfigureAwait(false);

        return UnitMatch.Select(
            units,
            settings.UnitFilter,
            settings.UnitSearch,
            settings.ShowInactiveUnits,
            settings.ShowUnloadedUnits);
    }

    private Task<string> ClearFavoritesAsync()
    {
        settings.ClearFavorites();
        return Task.FromResult(host.Tr("Favorite units cleared"));
    }
}
