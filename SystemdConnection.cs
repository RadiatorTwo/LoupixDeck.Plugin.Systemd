using LoupixDeck.PluginSdk;
using Tmds.DBus.Protocol;

namespace LoupixDeck.Plugin.Systemd;

/// <summary>
/// Owns the bus connection of one systemd instance and keeps it alive. The user instance is
/// reached over the session bus, the system instance over the system bus; nothing here knows
/// anything about units.
/// <para>
/// A missing bus is not an error: a machine without a user systemd instance, or a system bus the
/// user may not talk to, simply leaves the domain inactive.
/// </para>
/// </summary>
internal sealed class SystemdConnection(UnitDomain domain, IPluginLogger logger) : IDisposable
{
    public const string ManagerService = "org.freedesktop.systemd1";

    private const string BusService = "org.freedesktop.DBus";
    private const string BusPath = "/org/freedesktop/DBus";
    private const string BusInterface = "org.freedesktop.DBus";

    private static readonly TimeSpan[] ReconnectDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30)
    ];

    private readonly CancellationTokenSource _shutdown = new();

    private DBusConnection? _connection;
    private IDisposable? _nameOwnerWatch;

    /// <summary>The systemd instance this connection serves.</summary>
    public UnitDomain Domain { get; } = domain;

    /// <summary>The D-Bus helper, or null while the domain is not connected.</summary>
    public DBusClient? Client { get; private set; }

    /// <summary>True while the manager of this domain is usable.</summary>
    public bool IsConnected { get; private set; }

    /// <summary>
    /// Raised once the manager is usable, including after a reconnect and after systemd itself was
    /// restarted. Everything read from the bus has to be rebuilt here: the subscription, the object
    /// paths and every cached unit belong to the old manager.
    /// </summary>
    public event Action? ConnectionReady;

    /// <summary>Raised when the manager went away. The cached state is stale from then on.</summary>
    public event Action? ConnectionLost;

    /// <summary>Connects to the bus of this domain. Safe to call once, from Initialize.</summary>
    public async Task<bool> ConnectAsync()
    {
        string? address = Domain == UnitDomain.System ? DBusAddress.System : DBusAddress.Session;

        if (string.IsNullOrEmpty(address))
        {
            logger.Info($"Systemd: no {BusName()} bus address, the {DomainName()} instance stays inactive.");
            return false;
        }

        try
        {
            DBusConnection connection = new(address);
            await connection.ConnectAsync().ConfigureAwait(false);

            _connection = connection;
            Client = new DBusClient(connection, logger);
            _nameOwnerWatch = await WatchManagerOwnerAsync().ConfigureAwait(false);
            IsConnected = true;

            _ = Task.Run(() => MonitorConnectionAsync(connection), _shutdown.Token);
            RaiseConnectionReady();
            return true;
        }
        catch (Exception ex)
        {
            logger.Info($"Systemd: cannot use the {BusName()} bus ({ex.Message}), the {DomainName()} instance stays inactive.");
            Disconnect();
            return false;
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _nameOwnerWatch?.Dispose();
        _nameOwnerWatch = null;
        Disconnect();
        _shutdown.Dispose();
    }

    /// <summary>The bus name used in log text, which is the user-facing name of the bus itself.</summary>
    private string BusName() => Domain == UnitDomain.System ? "system" : "session";

    private string DomainName() => UnitDomainParser.ToParameterValue(Domain);

    private void RaiseConnectionReady()
    {
        try
        {
            ConnectionReady?.Invoke();
        }
        catch (Exception ex)
        {
            logger.Warn($"Systemd: a connection handler failed: {ex.Message}");
        }
    }

    private void RaiseConnectionLost()
    {
        try
        {
            ConnectionLost?.Invoke();
        }
        catch (Exception ex)
        {
            logger.Warn($"Systemd: a disconnect handler failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Watches the manager's bus name. systemd re-acquires it after <c>systemctl daemon-reexec</c>
    /// without the bus connection ever breaking, so this is the only way to notice that restart.
    /// </summary>
    private Task<IDisposable?> WatchManagerOwnerAsync()
    {
        DBusClient? client = Client;

        if (client is null)
        {
            return Task.FromResult<IDisposable?>(null);
        }

        return client.WatchSignalAsync(
            BusService,
            BusPath,
            BusInterface,
            "NameOwnerChanged",
            ReadNameOwnerChanged,
            OnManagerOwnerChanged);
    }

    private static (string Name, string NewOwner) ReadNameOwnerChanged(Message message, object? state)
    {
        Reader reader = message.GetBodyReader();
        string name = reader.ReadString();
        reader.ReadString(); // old owner
        string newOwner = reader.ReadString();
        return (name, newOwner);
    }

    private void OnManagerOwnerChanged((string Name, string NewOwner) change)
    {
        if (!string.Equals(change.Name, ManagerService, StringComparison.Ordinal))
        {
            return;
        }

        if (string.IsNullOrEmpty(change.NewOwner))
        {
            logger.Info($"Systemd: the {DomainName()} manager went away.");
            RaiseConnectionLost();
            return;
        }

        logger.Info($"Systemd: the {DomainName()} manager is back, the cached state is rebuilt.");
        RaiseConnectionReady();
    }

    private async Task MonitorConnectionAsync(DBusConnection connection)
    {
        int attempt = 0;

        while (!_shutdown.IsCancellationRequested)
        {
            Exception? error;
            try
            {
                error = await connection.DisconnectedAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                error = ex;
            }

            if (_shutdown.IsCancellationRequested)
            {
                return;
            }

            logger.Warn($"Systemd: the {BusName()} bus connection was lost ({error?.Message ?? "unknown reason"}), reconnecting.");
            IsConnected = false;
            RaiseConnectionLost();

            TimeSpan delay = ReconnectDelays[Math.Min(attempt, ReconnectDelays.Length - 1)];
            attempt++;

            try
            {
                await Task.Delay(delay, _shutdown.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // A successful reconnect starts its own monitor, so this loop hands over and ends.
            if (await ReconnectAsync().ConfigureAwait(false))
            {
                return;
            }
        }
    }

    private async Task<bool> ReconnectAsync()
    {
        _nameOwnerWatch?.Dispose();
        _nameOwnerWatch = null;
        _connection?.Dispose();
        _connection = null;
        Client = null;

        return await ConnectAsync().ConfigureAwait(false);
    }

    private void Disconnect()
    {
        IsConnected = false;
        Client = null;
        _connection?.Dispose();
        _connection = null;
    }
}
