using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Systemd;

/// <summary>
/// Pushes cached unit state onto the deck: it sets the active state of the favorite slot buttons
/// and asks the host to redraw every command bound to a unit that changed.
/// <para>
/// This is the only type that calls back into the host from a background thread, so every call is
/// guarded — a host that throws must not take the D-Bus read loop with it.
/// </para>
/// </summary>
internal sealed class SystemdStateBinder(
    IPluginHost host,
    UnitRegistry registry,
    IReadOnlyList<FavoriteSlotCommand> slots,
    IReadOnlyList<string> displayCommandNames) : IDisposable
{
    private bool _started;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        registry.UnitChanged += OnUnitChanged;
        _started = true;

        // Buttons created before the first read has to be brought up to date once.
        ReplayAll();
    }

    /// <summary>Applies the cached state of every favorite to its slot button.</summary>
    public void ReplayAll()
    {
        foreach (FavoriteSlotCommand slot in slots)
        {
            UnitId id = slot.Unit;

            if (id.IsValid)
            {
                Apply(slot, registry.Get(id));
            }
        }

        RefreshDisplays();
    }

    public void Dispose()
    {
        if (!_started)
        {
            return;
        }

        registry.UnitChanged -= OnUnitChanged;
        _started = false;
    }

    private void OnUnitChanged(UnitState state)
    {
        foreach (FavoriteSlotCommand slot in slots)
        {
            if (slot.Unit == state.Id)
            {
                Apply(slot, state);
            }
        }

        // A parameterized display button can sit on any unit, so the whole family is redrawn.
        RefreshDisplays();
    }

    private void Apply(FavoriteSlotCommand slot, UnitState state)
    {
        string commandName = slot.Descriptor.CommandName;
        string stateName = FavoriteSlotCommands.StateFor(state);

        try
        {
            host.SetActiveButtonState(commandName, stateName);
            host.RequestButtonRefresh(commandName);
        }
        catch (Exception ex)
        {
            host.Logger.Warn($"Systemd: cannot update {commandName}: {ex.Message}");
        }
    }

    private void RefreshDisplays()
    {
        foreach (string commandName in displayCommandNames)
        {
            try
            {
                host.RequestButtonRefresh(commandName);
            }
            catch (Exception ex)
            {
                host.Logger.Warn($"Systemd: cannot refresh {commandName}: {ex.Message}");
            }
        }
    }
}
