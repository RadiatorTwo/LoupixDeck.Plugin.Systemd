using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Systemd;

/// <summary>
/// Monitors and controls systemd units over the native D-Bus API of the user and the system
/// instance. It never runs systemctl, never asks for a password and never starts sudo.
/// </summary>
public sealed class SystemdPlugin : LoupixPlugin, IPluginSettingsPage
{
    private IPluginHost? _host;
    private SystemdSettings? _settings;
    private UnitRegistry? _registry;
    private SystemdSettingsPage? _settingsPage;

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
        }
        catch (Exception ex)
        {
            host.Logger.Error("Systemd: the plugin could not be initialized.", ex);
            Shutdown();
        }
    }

    public override IEnumerable<IPluginCommand> GetCommands() => [];

    public IReadOnlyList<PluginSettingDescriptor> SettingsSchema =>
        _settingsPage?.BuildSchema() ?? [];

    public IReadOnlyList<PluginSettingAction> SettingsActions =>
        _settingsPage?.BuildActions() ?? [];

    public void OnSettingsSaved()
    {
        SystemdSettings? settings = _settings;

        if (settings is null)
        {
            return;
        }

        // Turning the system instance on or off decides whether the system bus is opened at all,
        // so that switch is the one change the registry cannot absorb.
        if (settings.ShowSystemUnits != _systemDomainIncluded)
        {
            _registry?.Dispose();
            StartRegistry(settings);

            if (_host is not null)
            {
                _settingsPage = new SystemdSettingsPage(settings, _registry!, _host);
            }

            return;
        }

        UnitRegistry? registry = _registry;

        if (registry is null)
        {
            return;
        }

        registry.TimeoutMilliseconds = settings.DBusTimeoutMilliseconds;
        registry.TrackAll(settings.Favorites);
    }

    public override void Shutdown()
    {
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
