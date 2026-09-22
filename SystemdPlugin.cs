using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Systemd;

/// <summary>
/// Monitors and controls systemd units over the native D-Bus API of the user and the system
/// instance. It never runs systemctl, never asks for a password and never starts sudo.
/// </summary>
public sealed class SystemdPlugin : LoupixPlugin, IMenuContributor, IPluginSettingsPage
{
    private readonly List<IPluginCommand> _commands = [];

    private IPluginHost? _host;
    private SystemdSettings? _settings;
    private UnitRegistry? _registry;
    private SystemdSettingsPage? _settingsPage;
    private SystemdStateBinder? _binder;
    private SystemdMenu? _menu;

    /// <summary>True while the system instance is served, so a changed setting can be noticed.</summary>
    private bool _systemDomainIncluded;

    public override PluginMetadata Metadata { get; } = new()
    {
        Id = "systemd",
        Name = "Systemd",
        Version = new Version(1, 0, 0),
        SdkVersion = new Version(1, 24, 0),
        Author = "RadiatorTwo",
        Description = "Monitors and controls systemd user and system units over the native D-Bus API."
    };

    public override void Initialize(IPluginHost host)
    {
        _host = host;

        try
        {
            SystemdSettings settings = new(host.Settings);
            _settings = settings;

            StartRegistry(settings);
            _settingsPage = new SystemdSettingsPage(settings, _registry!, host);

            _commands.AddRange(UnitActionCommands.Create(_registry!, settings));
            _commands.Add(TimerUnits.CreateRunNow(_registry!, settings));

            IEnumerable<IPluginCommand> displays = SystemdDisplayCommands.Create(_registry!, settings);
            List<string> displayCommandNames = [];

            foreach (IPluginCommand display in displays)
            {
                _commands.Add(display);
                displayCommandNames.Add(display.Descriptor.CommandName);
            }

            IReadOnlyList<FavoriteSlotCommand> slots = FavoriteSlotCommands.Create(_registry!, settings);
            _commands.AddRange(slots);

            _commands.AddRange(FavoriteCommands.Create(_registry!, settings, host));

            _binder = new SystemdStateBinder(host, _registry!, slots, displayCommandNames);
            _binder.Start();

            _menu = new SystemdMenu(_registry!, settings, host);
        }
        catch (Exception ex)
        {
            host.Logger.Error("Systemd: the plugin could not be initialized.", ex);
            Shutdown();
        }
    }

    public override IEnumerable<IPluginCommand> GetCommands() => _commands;

    public override IReadOnlyList<CommandGroupDescriptor> GetCommandGroups() =>
    [
        new CommandGroupDescriptor
        {
            Group = SystemdCommands.Group,
            Icon = SystemdCommands.PickerGlyph,
            Description = "Start, stop and watch systemd units",
            Section = CommandGroupSection.Plugins
        }
    ];

    public async Task<IReadOnlyList<MenuNode>> GetMenuNodes(ButtonTargets target)
    {
        SystemdMenu? menu = _menu;

        if (menu is null)
        {
            return [];
        }

        return await menu.BuildAsync().ConfigureAwait(false);
    }

    public IReadOnlyList<PluginSettingDescriptor> SettingsSchema =>
        _settingsPage?.BuildSchema() ?? [];

    public IReadOnlyList<PluginSettingAction> SettingsActions =>
        _settingsPage?.BuildActions() ?? [];

    public void OnSettingsSaved()
    {
        SystemdSettings? settings = _settings;
        UnitRegistry? registry = _registry;

        if (settings is null || registry is null)
        {
            return;
        }

        registry.TimeoutMilliseconds = settings.DBusTimeoutMilliseconds;
        registry.TrackAll(settings.Favorites);

        // The favorites may now point at other units, so every slot button is brought up to date.
        _binder?.ReplayAll();

        if (settings.ShowSystemUnits == _systemDomainIncluded)
        {
            return;
        }

        // Opening or closing the system bus talks to D-Bus, so it must not hold up the settings
        // dialog. The registry object stays the same, because every command holds on to it.
        _systemDomainIncluded = settings.ShowSystemUnits;
        _ = Task.Run(async () =>
        {
            await registry.SetSystemDomainEnabledAsync(_systemDomainIncluded).ConfigureAwait(false);
            registry.TimeoutMilliseconds = settings.DBusTimeoutMilliseconds;
            registry.TrackAll(settings.Favorites);
        });
    }

    public override void Shutdown()
    {
        _binder?.Dispose();
        _binder = null;
        _menu = null;
        _commands.Clear();
        _registry?.Dispose();
        _registry = null;
        _settingsPage = null;
        _settings = null;
        _host = null;
    }

    private void StartRegistry(SystemdSettings settings)
    {
        IPluginHost? host = _host;

        if (host is null)
        {
            return;
        }

        _systemDomainIncluded = settings.ShowSystemUnits;

        UnitRegistry registry = new(host.Logger, _systemDomainIncluded);
        _registry = registry;

        registry.TrackAll(settings.Favorites);

        // Connecting talks to the bus, so it must not hold up the host's Initialize call.
        _ = Task.Run(async () =>
        {
            await registry.StartAsync().ConfigureAwait(false);
            registry.TimeoutMilliseconds = settings.DBusTimeoutMilliseconds;
        });
    }
}
