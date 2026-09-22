using System.Globalization;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Systemd;

/// <summary>
/// One favorite slot. The host binds a button state to a command name alone, so a command that
/// takes its unit from a parameter cannot carry per-unit states: two buttons would share one.
/// A slot therefore has a command name of its own and takes its unit from the favorites list,
/// which is what makes the states from the issue work.
/// </summary>
internal sealed class FavoriteSlotCommand(
    CommandDescriptor descriptor,
    int slotIndex,
    UnitRegistry registry,
    SystemdSettings settings) : UnitCommandBase(descriptor, registry, settings), IDisplayCommand
{
    public override ButtonTargets SupportedTargets => ButtonTargets.All;

    public TimeSpan UpdateInterval => TimeSpan.FromSeconds(1);

    /// <summary>The favorite this slot shows, or an invalid id while the slot is empty.</summary>
    public UnitId Unit => Settings.FavoriteAt(slotIndex);

    public string GetText(CommandContext ctx)
    {
        UnitId id = Unit;

        if (!id.IsValid)
        {
            return ctx.Host.Tr("Empty slot");
        }

        UnitState state = Registry.Get(id);
        string status = $"{SystemdCommands.StateGlyph(state)} {ctx.Host.Tr(SystemdCommands.StateText(state))}";
        return string.Join(Environment.NewLine, state.ShortName, status);
    }

    protected override Task ExecuteCore(CommandContext ctx)
    {
        UnitAction? action = Settings.FolderAction;

        if (action is null)
        {
            return Task.CompletedTask;
        }

        return RunActionAsync(ctx, Unit, action.Value);
    }
}

/// <summary>Builds the favorite slot commands.</summary>
internal static class FavoriteSlotCommands
{
    /// <summary>
    /// The states a slot button carries, in the order the host materializes them. Index 0 is the
    /// resting state. These names are persisted in the button config and must never change.
    /// </summary>
    public static readonly IReadOnlyList<ButtonStateDescriptor> States =
    [
        new ButtonStateDescriptor { Name = "Inactive", Description = "The unit is stopped" },
        new ButtonStateDescriptor { Name = "Activating", Description = "The unit is starting" },
        new ButtonStateDescriptor { Name = "Active", Description = "The unit is running" },
        new ButtonStateDescriptor { Name = "Deactivating", Description = "The unit is stopping" },
        new ButtonStateDescriptor { Name = "Reloading", Description = "The unit is reloading its configuration" },
        new ButtonStateDescriptor { Name = "Failed", Description = "The unit failed" },
        new ButtonStateDescriptor { Name = "NotFound", Description = "systemd does not know this unit" },
        new ButtonStateDescriptor { Name = "PermissionDenied", Description = "The last command was not allowed" }
    ];

    public static IReadOnlyList<FavoriteSlotCommand> Create(UnitRegistry registry, SystemdSettings settings)
    {
        List<FavoriteSlotCommand> commands = [];

        for (int slotIndex = 0; slotIndex < SystemdSettings.FavoriteSlotCount; slotIndex++)
        {
            string number = (slotIndex + 1).ToString("00", CultureInfo.InvariantCulture);

            CommandDescriptor descriptor = new()
            {
                CommandName = SystemdCommands.Prefix + "Favorite" + number,
                DisplayName = "Favorite Unit " + number,
                Group = SystemdCommands.Group,
                Icon = SystemdCommands.PickerGlyph,
                Description = "Show and control favorite unit " + number + ". The button follows the unit's state.",
                HiddenFromMenu = true,
                States = States
            };

            commands.Add(new FavoriteSlotCommand(descriptor, slotIndex, registry, settings));
        }

        return commands;
    }

    /// <summary>The state name a unit's snapshot maps to.</summary>
    public static string StateFor(UnitState state)
    {
        if (state.LastOutcome == UnitCallOutcome.PermissionDenied)
        {
            return "PermissionDenied";
        }

        if (state.Availability == UnitAvailability.NotFound)
        {
            return "NotFound";
        }

        return state.ActiveState switch
        {
            "active" => "Active",
            "activating" => "Activating",
            "deactivating" => "Deactivating",
            "reloading" => "Reloading",
            "failed" => "Failed",
            _ => "Inactive"
        };
    }
}
