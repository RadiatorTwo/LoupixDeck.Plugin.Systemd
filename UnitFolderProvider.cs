using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Systemd;

/// <summary>
/// The touch-screen folder that lists the favorite units with their state. Pressing an entry runs
/// the action from the settings. The back slot is reserved by the host, so it is skipped here.
/// </summary>
internal sealed class UnitFolderProvider(
    UnitRegistry registry,
    SystemdSettings settings,
    IPluginHost host) : FolderProviderBase
{
    private static readonly PluginColor ActiveColor = PluginColor.FromRgb(0x20, 0x60, 0x30);
    private static readonly PluginColor InactiveColor = PluginColor.FromRgb(0x20, 0x20, 0x40);
    private static readonly PluginColor FailedColor = PluginColor.FromRgb(0x70, 0x20, 0x20);
    private static readonly PluginColor BusyColor = PluginColor.FromRgb(0x70, 0x55, 0x10);
    private static readonly PluginColor MissingColor = PluginColor.FromRgb(0x20, 0x20, 0x20);
    private static readonly PluginColor DeniedColor = PluginColor.FromRgb(0x30, 0x40, 0x50);

    public override string Title => host.Tr("Systemd Units");

    public override IReadOnlyList<FolderEntry> BuildEntries()
    {
        FolderGridInfo grid = host.FolderGrid;
        IReadOnlyList<UnitId> favorites = settings.Favorites;

        if (favorites.Count == 0)
        {
            return
            [
                new FolderEntry
                {
                    SlotIndex = grid.SlotForIndex(0),
                    Text = host.Tr("No favorite units"),
                    BackColor = InactiveColor,
                    TextSize = 13
                }
            ];
        }

        List<FolderEntry> entries = [];

        for (int index = 0; index < favorites.Count; index++)
        {
            int slot = grid.SlotForIndex(index);

            if (slot < 0)
            {
                // The grid is full; the remaining favorites stay reachable through the command menu.
                break;
            }

            UnitId id = favorites[index];
            UnitState state = registry.Get(id);

            entries.Add(new FolderEntry
            {
                SlotIndex = slot,
                Text = BuildText(state),
                BackColor = ColorFor(state),
                TextSize = 13,
                Bold = state.IsActive,
                OnPress = () => PressAsync(id)
            });
        }

        return entries;
    }

    public override void OnEnter()
    {
        // The favorites must stay fresh while the folder is open, even if no button uses them.
        registry.TrackAll(settings.Favorites);
        registry.UnitChanged += OnUnitChanged;
    }

    public override void OnExit() => registry.UnitChanged -= OnUnitChanged;

    private void OnUnitChanged(UnitState state) => RaiseEntriesChanged();

    private async Task PressAsync(UnitId id)
    {
        UnitAction? action = settings.FolderAction;

        if (action is null)
        {
            // The folder is configured to only show the units, so a press just re-reads this one.
            await registry.RefreshAsync(id).ConfigureAwait(false);
            return;
        }

        await registry.RunAsync(id, action.Value, settings.CommandTimeout).ConfigureAwait(false);
        RaiseEntriesChanged();
    }

    private string BuildText(UnitState state)
    {
        string status = $"{SystemdCommands.StateGlyph(state)} {host.Tr(SystemdCommands.StateText(state))}";
        return string.Join(Environment.NewLine, state.ShortName, status);
    }

    private static PluginColor ColorFor(UnitState state)
    {
        if (state.LastOutcome == UnitCallOutcome.PermissionDenied)
        {
            return DeniedColor;
        }

        if (state.Availability is UnitAvailability.NotFound or UnitAvailability.Unavailable)
        {
            return MissingColor;
        }

        return state.ActiveState switch
        {
            "active" => ActiveColor,
            "failed" => FailedColor,
            "activating" or "deactivating" or "reloading" => BusyColor,
            _ => InactiveColor
        };
    }
}
