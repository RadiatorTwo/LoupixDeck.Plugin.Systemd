using System.Text;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Systemd;

/// <summary>
/// The plugin's settings form. The host renders the descriptors, so only the values are declared
/// here. Labels and descriptions are English keys the host translates.
/// <para>
/// The SDK has no pick list and no search field, so the choices are text tokens and the unit list
/// is offered through the "List units" button instead.
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
            Description = "The unit types offered in the command menu. This version lists services only.",
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
            Label = "Clear favorite units",
            Invoke = ClearFavoritesAsync
        }
    ];

    /// <summary>
    /// Prints the units of every served instance. This is what the missing search field is
    /// replaced with: the names can be copied straight into the favorites field.
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

            IReadOnlyList<UnitListEntry> units = await registry.ListServicesAsync(domain).ConfigureAwait(false);
            builder.AppendLine($"{UnitDomainParser.ToEnglishText(domain)}: {units.Count}");

            foreach (UnitListEntry unit in units)
            {
                if (printed >= ListLimit)
                {
                    builder.AppendLine("…");
                    return builder.ToString();
                }

                builder.AppendLine($"{UnitDomainParser.ToParameterValue(domain)}:{unit.Name} — {unit.ActiveState}");
                printed++;
            }
        }

        return builder.Length == 0 ? host.Tr("No units") : builder.ToString();
    }

    private Task<string> ClearFavoritesAsync()
    {
        settings.ClearFavorites();
        return Task.FromResult(host.Tr("Favorite units cleared"));
    }
}
