using System.Collections.Concurrent;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Systemd;

/// <summary>
/// The in-memory picture of every unit the plugin cares about, across both systemd instances.
/// <para>
/// Display commands are polled synchronously, so they must never wait for D-Bus. Everything they
/// read comes from here: signals mark a unit dirty, a debounce timer re-reads the dirty units, and
/// the result is published as a new snapshot.
/// </para>
/// </summary>
internal sealed class UnitRegistry : IDisposable
{
    /// <summary>A unit can push several property changes at once, so re-reads are coalesced.</summary>
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>How long a listing is reused before the bus is asked again.</summary>
    private static readonly TimeSpan ListCacheLifetime = TimeSpan.FromSeconds(30);

    private readonly IPluginLogger _logger;
    private readonly Dictionary<UnitDomain, DomainContext> _domains = [];
    private readonly ConcurrentDictionary<UnitId, UnitState> _units = [];
    private readonly ConcurrentDictionary<UnitId, byte> _tracked = [];
    private readonly ConcurrentDictionary<UnitId, byte> _dirty = [];
    private readonly Lock _refreshGate = new();

    private Timer? _refreshTimer;
    private bool _refreshScheduled;
    private bool _disposed;

    public UnitRegistry(IPluginLogger logger, bool includeSystemDomain)
    {
        _logger = logger;

        AddDomain(UnitDomain.User);

        if (includeSystemDomain)
        {
            AddDomain(UnitDomain.System);
        }

        _refreshTimer = new Timer(_ => RunRefresh(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Raised with the new snapshot whenever a tracked unit changed.</summary>
    public event Action<UnitState>? UnitChanged;

    /// <summary>Raised when systemd added or removed units, so cached listings are stale.</summary>
    public event Action? UnitSetChanged;

    /// <summary>The domains this registry actually serves.</summary>
    public IReadOnlyCollection<UnitDomain> Domains => _domains.Keys;

    /// <summary>The per-call D-Bus timeout applied to every domain.</summary>
    public int TimeoutMilliseconds
    {
        set
        {
            foreach (DomainContext domain in _domains.Values)
            {
                if (domain.Connection.Client is { } client)
                {
                    client.TimeoutMilliseconds = value;
                }
            }
        }
    }

    /// <summary>Connects every domain and subscribes to its manager. Failures leave a domain inactive.</summary>
    public async Task StartAsync()
    {
        foreach (DomainContext domain in _domains.Values.ToArray())
        {
            await domain.Connection.ConnectAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Turns the system instance on or off while the plugin runs. Off closes the system bus and
    /// forgets everything read from it; on opens it and reads the tracked units again. The
    /// registry itself stays the same object, because the commands hold on to it.
    /// </summary>
    public async Task SetSystemDomainEnabledAsync(bool enabled)
    {
        if (_disposed || enabled == _domains.ContainsKey(UnitDomain.System))
        {
            return;
        }

        if (!enabled)
        {
            if (_domains.Remove(UnitDomain.System, out DomainContext? removed))
            {
                removed.Jobs.Dispose();
                removed.Manager.Dispose();
                removed.Connection.Dispose();
            }

            foreach (UnitId id in _tracked.Keys.Where(id => id.Domain == UnitDomain.System))
            {
                _units.TryRemove(id, out _);
            }

            return;
        }

        DomainContext context = AddDomain(UnitDomain.System);
        await context.Connection.ConnectAsync().ConfigureAwait(false);
    }

    /// <summary>True while the manager of that domain can be reached.</summary>
    public bool IsAvailable(UnitDomain domain) =>
        _domains.TryGetValue(domain, out DomainContext? context) && context.Connection.IsConnected;

    /// <summary>
    /// Reads the cached snapshot of a unit. Never blocks, so this is what display commands and the
    /// folder use. An untracked unit is registered and read in the background.
    /// </summary>
    public UnitState Get(UnitId id)
    {
        if (!id.IsValid)
        {
            return UnitState.Pending(id);
        }

        if (_units.TryGetValue(id, out UnitState? state))
        {
            return state;
        }

        Track(id);
        return UnitState.Pending(id);
    }

    /// <summary>Adds a unit to the tracked set and schedules the first read.</summary>
    public void Track(UnitId id)
    {
        if (!id.IsValid || !_domains.ContainsKey(id.Domain))
        {
            return;
        }

        _tracked.TryAdd(id, 0);
        MarkDirty(id);
    }

    /// <summary>Adds several units at once, which is what the favorites and the folder do.</summary>
    public void TrackAll(IEnumerable<UnitId> ids)
    {
        foreach (UnitId id in ids)
        {
            Track(id);
        }
    }

    /// <summary>Re-reads one unit right away, without waiting for the debounce timer.</summary>
    public Task RefreshAsync(UnitId id)
    {
        _tracked.TryAdd(id, 0);
        return ReadUnitAsync(id);
    }

    /// <summary>
    /// Records how the user's last command on a unit ended, so a denial can be shown without
    /// touching the state systemd reports.
    /// </summary>
    public void SetLastOutcome(UnitId id, UnitCallOutcome outcome)
    {
        UnitState current = Get(id);
        Publish(current with { LastOutcome = outcome, LastUpdate = DateTimeOffset.UtcNow });
    }

    /// <summary>
    /// Runs an action on a unit and waits for the systemd job it produced. The call itself
    /// succeeding says nothing about the unit, so the job result decides the outcome; afterwards
    /// the unit is re-read in every case, including after a timeout.
    /// </summary>
    public async Task<UnitActionResult> RunAsync(UnitId id, UnitAction action, TimeSpan timeout)
    {
        if (!id.IsValid || !_domains.TryGetValue(id.Domain, out DomainContext? context))
        {
            return new UnitActionResult(UnitCallOutcome.NotFound, string.Empty);
        }

        if (!context.Connection.IsConnected)
        {
            return Finish(id, new UnitActionResult(UnitCallOutcome.Unavailable, string.Empty));
        }

        Track(id);

        if (action == UnitAction.ResetFailed)
        {
            UnitCallOutcome reset = await context.Manager.ResetFailedUnitAsync(id.Name).ConfigureAwait(false);
            return Finish(id, new UnitActionResult(reset, string.Empty));
        }

        UnitAction effective = action == UnitAction.Toggle
            ? await ResolveToggleAsync(id).ConfigureAwait(false)
            : action;

        DBusResult<string> job = effective switch
        {
            UnitAction.Start => await context.Manager.StartUnitAsync(id.Name).ConfigureAwait(false),
            UnitAction.Stop => await context.Manager.StopUnitAsync(id.Name).ConfigureAwait(false),
            UnitAction.Restart => await context.Manager.RestartUnitAsync(id.Name).ConfigureAwait(false),
            _ => await context.Manager.ReloadUnitAsync(id.Name).ConfigureAwait(false)
        };

        if (!job.IsSuccess)
        {
            return Finish(id, new UnitActionResult(job.Outcome, string.Empty));
        }

        string result = await context.Jobs.WaitAsync(job.Value, timeout).ConfigureAwait(false);
        return Finish(id, new UnitActionResult(JobTracker.ToOutcome(result), result));
    }

    /// <summary>
    /// Decides what a toggle does. The cached state may predate the press, so a unit that has not
    /// been read yet is read first rather than guessed.
    /// </summary>
    private async Task<UnitAction> ResolveToggleAsync(UnitId id)
    {
        UnitState state = Get(id);

        if (state.Availability == UnitAvailability.Unknown)
        {
            await RefreshAsync(id).ConfigureAwait(false);
            state = Get(id);
        }

        return state.IsActive ? UnitAction.Stop : UnitAction.Start;
    }

    /// <summary>Records the outcome and re-reads the unit, whatever happened.</summary>
    private UnitActionResult Finish(UnitId id, UnitActionResult result)
    {
        SetLastOutcome(id, result.Outcome);
        _ = Task.Run(() => RefreshAsync(id));
        return result;
    }

    /// <summary>
    /// Lists the service units of a domain. The result is cached, because the command menu is built
    /// from it and the host gives a menu contributor only a few seconds.
    /// </summary>
    public async Task<IReadOnlyList<UnitListEntry>> ListServicesAsync(UnitDomain domain)
    {
        if (!_domains.TryGetValue(domain, out DomainContext? context) || !context.Connection.IsConnected)
        {
            return [];
        }

        if (context.ListedAt is { } listedAt && DateTimeOffset.UtcNow - listedAt < ListCacheLifetime)
        {
            return context.Listing;
        }

        IReadOnlyList<UnitListEntry> listing = await context.Manager.ListUnitsAsync(["*.service"]).ConfigureAwait(false);

        // Remember the object paths right away; a property change for one of them can arrive at
        // any time and would otherwise have to be resolved by unescaping the path.
        foreach (UnitListEntry entry in listing)
        {
            if (entry.ObjectPath.Length > 0)
            {
                context.PathToUnit[entry.ObjectPath] = new UnitId(domain, entry.Name);
            }
        }

        context.Listing = listing;
        context.ListedAt = DateTimeOffset.UtcNow;
        return listing;
    }

    /// <summary>The manager of a domain, or null when that domain is not served.</summary>
    public SystemdManager? GetManager(UnitDomain domain) =>
        _domains.TryGetValue(domain, out DomainContext? context) ? context.Manager : null;

    public void Dispose()
    {
        _disposed = true;

        _refreshTimer?.Dispose();
        _refreshTimer = null;

        foreach (DomainContext domain in _domains.Values)
        {
            domain.Jobs.Dispose();
            domain.Manager.Dispose();
            domain.Connection.Dispose();
        }

        _domains.Clear();
    }

    private DomainContext AddDomain(UnitDomain domain)
    {
        SystemdConnection connection = new(domain, _logger);
        SystemdManager manager = new(connection, _logger);
        DomainContext context = new(connection, manager);

        connection.ConnectionReady += () => OnConnectionReady(context);
        connection.ConnectionLost += () => OnConnectionLost(context);

        manager.UnitPropertiesChanged += path => OnUnitPropertiesChanged(context, path);
        manager.UnitSetChanged += _ => OnUnitSetChanged(context);
        manager.Reloading += reloading => OnReloading(context, reloading);
        manager.JobRemoved += (jobPath, result) => context.Jobs.Complete(jobPath, result);

        _domains[domain] = context;
        return context;
    }

    private void OnConnectionReady(DomainContext context)
    {
        // Paths and the listing belong to the manager that just went away.
        context.PathToUnit.Clear();
        context.InvalidateListing();

        _ = Task.Run(async () =>
        {
            await context.Manager.StartAsync().ConfigureAwait(false);
            MarkAllDirty(context.Connection.Domain);
        });
    }

    private void OnConnectionLost(DomainContext context)
    {
        context.PathToUnit.Clear();
        context.InvalidateListing();

        // The jobs of the old manager will never report back.
        context.Jobs.Reset();

        foreach (UnitId id in _tracked.Keys)
        {
            if (id.Domain != context.Connection.Domain)
            {
                continue;
            }

            Publish(Get(id) with
            {
                Availability = UnitAvailability.Unavailable,
                LastUpdate = DateTimeOffset.UtcNow
            });
        }
    }

    private void OnUnitPropertiesChanged(DomainContext context, string objectPath)
    {
        if (objectPath.Length == 0)
        {
            return;
        }

        if (!context.PathToUnit.TryGetValue(objectPath, out UnitId id))
        {
            string name = SystemdUnitPath.Unescape(objectPath);

            if (name.Length == 0)
            {
                return;
            }

            id = new UnitId(context.Connection.Domain, name);
        }

        // Only units something is actually bound to are worth a round trip.
        if (_tracked.ContainsKey(id))
        {
            MarkDirty(id);
        }
    }

    private void OnUnitSetChanged(DomainContext context)
    {
        context.InvalidateListing();
        RaiseUnitSetChanged();
    }

    private void OnReloading(DomainContext context, bool reloading)
    {
        context.IsReloading = reloading;

        if (reloading)
        {
            return;
        }

        // A daemon-reload can change the unit file state and can drop units entirely, so nothing
        // that was read before it can be trusted.
        context.PathToUnit.Clear();
        context.InvalidateListing();
        MarkAllDirty(context.Connection.Domain);
        RaiseUnitSetChanged();
    }

    private void MarkAllDirty(UnitDomain domain)
    {
        foreach (UnitId id in _tracked.Keys)
        {
            if (id.Domain == domain)
            {
                MarkDirty(id);
            }
        }
    }

    private void MarkDirty(UnitId id)
    {
        if (_disposed)
        {
            return;
        }

        _dirty[id] = 0;

        lock (_refreshGate)
        {
            if (_refreshScheduled)
            {
                return;
            }

            _refreshScheduled = true;
            _refreshTimer?.Change(RefreshDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void RunRefresh()
    {
        lock (_refreshGate)
        {
            _refreshScheduled = false;
        }

        UnitId[] pending = [.. _dirty.Keys];

        foreach (UnitId id in pending)
        {
            _dirty.TryRemove(id, out _);
        }

        _ = Task.Run(async () =>
        {
            foreach (UnitId id in pending)
            {
                await ReadUnitAsync(id).ConfigureAwait(false);
            }
        });
    }

    private async Task ReadUnitAsync(UnitId id)
    {
        if (_disposed || !id.IsValid || !_domains.TryGetValue(id.Domain, out DomainContext? context))
        {
            return;
        }

        UnitState current = _units.TryGetValue(id, out UnitState? cached) ? cached : UnitState.Pending(id);

        if (!context.Connection.IsConnected)
        {
            Publish(current with { Availability = UnitAvailability.Unavailable, LastUpdate = DateTimeOffset.UtcNow });
            return;
        }

        // While a daemon-reload runs, anything read is about to be replaced. The end of the reload
        // marks every tracked unit dirty again, so skipping here costs nothing.
        if (context.IsReloading)
        {
            return;
        }

        string objectPath = current.ObjectPath;

        if (objectPath.Length == 0)
        {
            DBusResult<string> path = await context.Manager.LoadUnitAsync(id.Name).ConfigureAwait(false);

            if (!path.IsSuccess || path.Value.Length == 0)
            {
                Publish(current with
                {
                    Availability = ToAvailability(path.Outcome),
                    LastUpdate = DateTimeOffset.UtcNow
                });
                return;
            }

            objectPath = path.Value;
            context.PathToUnit[objectPath] = id;
        }

        DBusResult<UnitProperties> properties = await context.Manager.GetPropertiesAsync(objectPath).ConfigureAwait(false);

        if (!properties.IsSuccess)
        {
            Publish(current with
            {
                ObjectPath = objectPath,
                Availability = ToAvailability(properties.Outcome),
                LastUpdate = DateTimeOffset.UtcNow
            });
            return;
        }

        UnitProperties value = properties.Value;

        Publish(current with
        {
            ObjectPath = objectPath,
            Description = value.Description,
            LoadState = value.LoadState,
            ActiveState = value.ActiveState,
            SubState = value.SubState,
            UnitFileState = value.UnitFileState,
            ActiveEnter = value.ActiveEnter,
            MainPid = value.MainPid,
            Result = value.Result,
            CanStart = value.CanStart,
            CanStop = value.CanStop,
            CanReload = value.CanReload,
            // A unit systemd loads but does not find on disk reports load state not-found.
            Availability = string.Equals(value.LoadState, "not-found", StringComparison.Ordinal)
                ? UnitAvailability.NotFound
                : UnitAvailability.Known,
            LastUpdate = DateTimeOffset.UtcNow
        });
    }

    private static UnitAvailability ToAvailability(UnitCallOutcome outcome)
    {
        return outcome switch
        {
            UnitCallOutcome.Ok => UnitAvailability.Known,
            UnitCallOutcome.PermissionDenied => UnitAvailability.PermissionDenied,
            UnitCallOutcome.NotFound => UnitAvailability.NotFound,
            _ => UnitAvailability.Unavailable
        };
    }

    private void Publish(UnitState state)
    {
        bool hadState = _units.TryGetValue(state.Id, out UnitState? previous);
        _units[state.Id] = state;

        // Several signals can describe the same change, and a command re-reads on top of them.
        // Only a real difference is worth waking the buttons up for.
        if (hadState && previous! with { LastUpdate = state.LastUpdate } == state)
        {
            return;
        }

        try
        {
            UnitChanged?.Invoke(state);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Systemd: a unit handler failed: {ex.Message}");
        }
    }

    private void RaiseUnitSetChanged()
    {
        try
        {
            UnitSetChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.Warn($"Systemd: a unit set handler failed: {ex.Message}");
        }
    }

    /// <summary>Everything the registry keeps for one systemd instance.</summary>
    private sealed class DomainContext(SystemdConnection connection, SystemdManager manager)
    {
        public SystemdConnection Connection { get; } = connection;

        public SystemdManager Manager { get; } = manager;

        /// <summary>Object path to unit, so a property change does not have to unescape the path.</summary>
        public ConcurrentDictionary<string, UnitId> PathToUnit { get; } = new(StringComparer.Ordinal);

        public IReadOnlyList<UnitListEntry> Listing { get; set; } = [];

        public DateTimeOffset? ListedAt { get; set; }

        public bool IsReloading { get; set; }

        /// <summary>Job paths are only unique within one instance, so every domain tracks its own.</summary>
        public JobTracker Jobs { get; } = new();

        public void InvalidateListing()
        {
            Listing = [];
            ListedAt = null;
        }
    }
}
