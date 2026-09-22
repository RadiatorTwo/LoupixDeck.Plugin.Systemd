using System.Diagnostics;
using LoupixDeck.PluginSdk;
using Tmds.DBus.Protocol;

namespace LoupixDeck.Plugin.Systemd;

/// <summary>One row of org.freedesktop.systemd1.Manager.ListUnitsByPatterns.</summary>
internal readonly record struct UnitListEntry(
    string Name,
    string Description,
    string LoadState,
    string ActiveState,
    string SubState,
    string ObjectPath);

/// <summary>The properties of one unit, read in a single pass.</summary>
internal readonly record struct UnitProperties(
    string Description,
    string LoadState,
    string ActiveState,
    string SubState,
    string UnitFileState,
    DateTimeOffset? ActiveEnter,
    uint MainPid,
    string Result,
    bool CanStart,
    bool CanStop,
    bool CanReload,
    DateTimeOffset? NextElapse,
    DateTimeOffset? LastTrigger,
    string TriggerUnit);

/// <summary>
/// Talks to org.freedesktop.systemd1.Manager of one instance: lists units, reads their properties,
/// runs the runtime and unit file actions and forwards the manager's signals. It holds no unit state of its
/// own — that is the registry's job.
/// </summary>
internal sealed class SystemdManager(SystemdConnection connection, IPluginLogger logger) : IDisposable
{
    public const string Service = SystemdConnection.ManagerService;
    public const string ManagerPath = "/org/freedesktop/systemd1";
    public const string ManagerInterface = "org.freedesktop.systemd1.Manager";
    public const string UnitInterface = "org.freedesktop.systemd1.Unit";
    public const string ServiceInterface = "org.freedesktop.systemd1.Service";
    public const string TimerInterface = "org.freedesktop.systemd1.Timer";

    /// <summary>The job mode every action uses. "replace" is what systemctl does by default.</summary>
    private const string JobMode = "replace";

    private readonly List<IDisposable> _watches = [];

    public UnitDomain Domain => connection.Domain;

    public bool IsConnected => connection.IsConnected;

    /// <summary>Raised when a job finished, with the job path and systemd's result string.</summary>
    public event Action<string, string>? JobRemoved;

    /// <summary>Raised with the object path of a unit whose properties changed.</summary>
    public event Action<string>? UnitPropertiesChanged;

    /// <summary>Raised when systemd added or removed a unit, with its name.</summary>
    public event Action<string>? UnitSetChanged;

    /// <summary>Raised when a daemon-reload starts (true) and when it finished (false).</summary>
    public event Action<bool>? Reloading;

    /// <summary>
    /// Subscribes to the manager and installs the signal watches. systemd only emits unit property
    /// changes while at least one client is subscribed, so this is required for the cache, not just
    /// for job tracking.
    /// </summary>
    public async Task<bool> StartAsync()
    {
        DBusClient? client = connection.Client;

        if (client is null)
        {
            return false;
        }

        UnitCallOutcome outcome = await client
            .CallAsync(Service, ManagerPath, ManagerInterface, "Subscribe")
            .ConfigureAwait(false);

        if (outcome != UnitCallOutcome.Ok)
        {
            logger.Info($"Systemd: cannot subscribe to the {UnitDomainParser.ToParameterValue(Domain)} manager, state updates stay off.");
            return false;
        }

        await WatchSignalsAsync(client).ConfigureAwait(false);
        return true;
    }

    /// <summary>Drops every signal watch. Called before a re-subscribe and on shutdown.</summary>
    public void Stop()
    {
        foreach (IDisposable watch in _watches)
        {
            watch.Dispose();
        }

        _watches.Clear();
    }

    public void Dispose() => Stop();

    /// <summary>Lists the units matching the given name patterns, in whatever state they are.</summary>
    public async Task<IReadOnlyList<UnitListEntry>> ListUnitsAsync(IReadOnlyList<string> patterns)
    {
        DBusClient? client = connection.Client;

        if (client is null)
        {
            return [];
        }

        DBusResult<List<UnitListEntry>> result = await client.CallAsync(
            Service,
            ManagerPath,
            ManagerInterface,
            "ListUnitsByPatterns",
            ReadUnitList,
            [],
            "asas",
            (ref MessageWriter writer) =>
            {
                writer.WriteArray(Array.Empty<string>()); // every state
                writer.WriteArray(patterns.ToArray());
            }).ConfigureAwait(false);

        return result.Value;
    }

    /// <summary>
    /// Resolves the object path of a unit, loading it when needed. LoadUnit is used instead of
    /// GetUnit so a unit that exists on disk but is not loaded yet still resolves.
    /// </summary>
    public async Task<DBusResult<string>> LoadUnitAsync(string unitName)
    {
        DBusClient? client = connection.Client;

        if (client is null)
        {
            return new DBusResult<string>(string.Empty, UnitCallOutcome.Unavailable);
        }

        return await client.CallAsync(
            Service,
            ManagerPath,
            ManagerInterface,
            "LoadUnit",
            DBusClient.ReadObjectPath,
            string.Empty,
            "s",
            (ref MessageWriter writer) => writer.WriteString(unitName)).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the Unit and Service properties of one object path in one go, and the Timer
    /// properties as well when <paramref name="isTimer"/> says the unit is one.
    /// </summary>
    public async Task<DBusResult<UnitProperties>> GetPropertiesAsync(string objectPath, bool isTimer)
    {
        DBusClient? client = connection.Client;

        if (client is null)
        {
            return new DBusResult<UnitProperties>(default, UnitCallOutcome.Unavailable);
        }

        DBusResult<Dictionary<string, VariantValue>> unit = await client
            .GetAllPropertiesAsync(Service, objectPath, UnitInterface)
            .ConfigureAwait(false);

        if (!unit.IsSuccess)
        {
            return new DBusResult<UnitProperties>(default, unit.Outcome);
        }

        // Only services carry these, and a unit of another type simply reports nothing.
        DBusResult<Dictionary<string, VariantValue>> service = await client
            .GetAllPropertiesAsync(Service, objectPath, ServiceInterface)
            .ConfigureAwait(false);

        Dictionary<string, VariantValue> timer = [];

        if (isTimer)
        {
            DBusResult<Dictionary<string, VariantValue>> timerResult = await client
                .GetAllPropertiesAsync(Service, objectPath, TimerInterface)
                .ConfigureAwait(false);
            timer = timerResult.Value;
        }

        UnitProperties properties = new(
            ReadString(unit.Value, "Description"),
            ReadString(unit.Value, "LoadState"),
            ReadString(unit.Value, "ActiveState"),
            ReadString(unit.Value, "SubState"),
            ReadString(unit.Value, "UnitFileState"),
            ReadTimestamp(unit.Value, "ActiveEnterTimestamp"),
            ReadUInt32(service.Value, "MainPID"),
            ReadString(service.Value, "Result"),
            ReadBool(unit.Value, "CanStart"),
            ReadBool(unit.Value, "CanStop"),
            ReadBool(unit.Value, "CanReload"),
            ReadNextElapse(timer),
            ReadTimestamp(timer, "LastTriggerUSec"),
            ReadString(timer, "Unit"));

        return new DBusResult<UnitProperties>(properties, UnitCallOutcome.Ok);
    }

    /// <summary>Starts a unit and returns the path of the job systemd queued for it.</summary>
    public Task<DBusResult<string>> StartUnitAsync(string unitName) => CallJobAsync("StartUnit", unitName);

    public Task<DBusResult<string>> StopUnitAsync(string unitName) => CallJobAsync("StopUnit", unitName);

    public Task<DBusResult<string>> RestartUnitAsync(string unitName) => CallJobAsync("RestartUnit", unitName);

    public Task<DBusResult<string>> ReloadUnitAsync(string unitName) => CallJobAsync("ReloadUnit", unitName);

    /// <summary>Clears the failed state of a unit. This call produces no job.</summary>
    public async Task<UnitCallOutcome> ResetFailedUnitAsync(string unitName)
    {
        DBusClient? client = connection.Client;

        if (client is null)
        {
            return UnitCallOutcome.Unavailable;
        }

        return await client.CallAsync(
            Service,
            ManagerPath,
            ManagerInterface,
            "ResetFailedUnit",
            "s",
            (ref MessageWriter writer) => writer.WriteString(unitName)).ConfigureAwait(false);
    }

    /// <summary>
    /// Changes the unit file state: enable, disable, mask or unmask. The change is persistent
    /// (runtime false) and never forced, exactly like a plain <c>systemctl enable</c>. Returns how
    /// many symlinks systemd created or removed; 0 means the unit already was in that state, or,
    /// for enable, that it has no install section to act on.
    /// </summary>
    public async Task<DBusResult<int>> ChangeUnitFileAsync(UnitAction action, string unitName)
    {
        DBusClient? client = connection.Client;

        if (client is null)
        {
            return new DBusResult<int>(0, UnitCallOutcome.Unavailable);
        }

        string[] files = [unitName];

        // Enable and mask take a force flag, disable and unmask do not; enable also returns
        // whether the unit carries install information in front of the change list.
        return action switch
        {
            UnitAction.Enable => await client.CallAsync(
                Service, ManagerPath, ManagerInterface, "EnableUnitFiles",
                ReadEnableChanges, 0, "asbb",
                (ref MessageWriter writer) =>
                {
                    writer.WriteArray(files);
                    writer.WriteBool(false);
                    writer.WriteBool(false);
                }).ConfigureAwait(false),
            UnitAction.Mask => await client.CallAsync(
                Service, ManagerPath, ManagerInterface, "MaskUnitFiles",
                ReadChanges, 0, "asbb",
                (ref MessageWriter writer) =>
                {
                    writer.WriteArray(files);
                    writer.WriteBool(false);
                    writer.WriteBool(false);
                }).ConfigureAwait(false),
            UnitAction.Disable => await client.CallAsync(
                Service, ManagerPath, ManagerInterface, "DisableUnitFiles",
                ReadChanges, 0, "asb",
                (ref MessageWriter writer) =>
                {
                    writer.WriteArray(files);
                    writer.WriteBool(false);
                }).ConfigureAwait(false),
            UnitAction.Unmask => await client.CallAsync(
                Service, ManagerPath, ManagerInterface, "UnmaskUnitFiles",
                ReadChanges, 0, "asb",
                (ref MessageWriter writer) =>
                {
                    writer.WriteArray(files);
                    writer.WriteBool(false);
                }).ConfigureAwait(false),
            _ => new DBusResult<int>(0, UnitCallOutcome.Failed)
        };
    }

    /// <summary>
    /// Runs a daemon-reload, which is what makes a changed unit file state visible. systemctl does
    /// the same after every enable, disable, mask and unmask. The call returns once the reload is
    /// done, which can take longer than an ordinary call.
    /// </summary>
    public async Task<UnitCallOutcome> ReloadDaemonAsync(TimeSpan timeout)
    {
        DBusClient? client = connection.Client;

        if (client is null)
        {
            return UnitCallOutcome.Unavailable;
        }

        return await client.CallAsync(Service, ManagerPath, ManagerInterface, "Reload", timeout: timeout).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs one of the job-producing actions. The interactive authorization flag is never set, so
    /// PolicyKit answers with a denial instead of prompting the user for a password.
    /// </summary>
    private async Task<DBusResult<string>> CallJobAsync(string member, string unitName)
    {
        DBusClient? client = connection.Client;

        if (client is null)
        {
            return new DBusResult<string>(string.Empty, UnitCallOutcome.Unavailable);
        }

        return await client.CallAsync(
            Service,
            ManagerPath,
            ManagerInterface,
            member,
            DBusClient.ReadObjectPath,
            string.Empty,
            "ss",
            (ref MessageWriter writer) =>
            {
                writer.WriteString(unitName);
                writer.WriteString(JobMode);
            }).ConfigureAwait(false);
    }

    private async Task WatchSignalsAsync(DBusClient client)
    {
        Stop();

        Add(await client.WatchSignalAsync(
            Service, ManagerPath, ManagerInterface, "JobRemoved",
            ReadJobRemoved,
            job => JobRemoved?.Invoke(job.JobPath, job.Result)).ConfigureAwait(false));

        Add(await client.WatchSignalAsync(
            Service, ManagerPath, ManagerInterface, "UnitNew",
            ReadUnitName,
            name => UnitSetChanged?.Invoke(name)).ConfigureAwait(false));

        Add(await client.WatchSignalAsync(
            Service, ManagerPath, ManagerInterface, "UnitRemoved",
            ReadUnitName,
            name => UnitSetChanged?.Invoke(name)).ConfigureAwait(false));

        Add(await client.WatchSignalAsync(
            Service, ManagerPath, ManagerInterface, "Reloading",
            ReadReloading,
            active => Reloading?.Invoke(active)).ConfigureAwait(false));

        // One rule for every unit object: systemd publishes property changes per unit, and
        // subscribing per unit would mean one match rule per tracked unit.
        MatchRule propertiesRule = new()
        {
            Type = MessageType.Signal,
            Sender = Service,
            PathNamespace = "/org/freedesktop/systemd1/unit",
            Interface = DBusClient.PropertiesInterface,
            Member = "PropertiesChanged"
        };

        Add(await client.WatchMatchAsync(
            propertiesRule,
            ReadChangedObjectPath,
            path => UnitPropertiesChanged?.Invoke(path)).ConfigureAwait(false));
    }

    private void Add(IDisposable? watch)
    {
        if (watch is not null)
        {
            _watches.Add(watch);
        }
    }

    private static List<UnitListEntry> ReadUnitList(Message message, object? state)
    {
        Reader reader = message.GetBodyReader();
        List<UnitListEntry> entries = [];

        ArrayEnd end = reader.ReadArrayStart(DBusType.Struct);
        while (reader.HasNext(end))
        {
            string name = reader.ReadString();
            string description = reader.ReadString();
            string loadState = reader.ReadString();
            string activeState = reader.ReadString();
            string subState = reader.ReadString();
            reader.ReadString(); // the unit this one follows
            string objectPath = reader.ReadObjectPathAsString();
            reader.ReadUInt32(); // job id
            reader.ReadString(); // job type
            reader.ReadObjectPathAsString(); // job path

            entries.Add(new UnitListEntry(name, description, loadState, activeState, subState, objectPath));
        }

        return entries;
    }

    /// <summary>Counts the entries of the a(sss) change list a unit file call returns.</summary>
    private static int ReadChanges(Message message, object? state)
    {
        Reader reader = message.GetBodyReader();
        return CountChanges(ref reader);
    }

    /// <summary>EnableUnitFiles puts a carries-install-info flag in front of the change list.</summary>
    private static int ReadEnableChanges(Message message, object? state)
    {
        Reader reader = message.GetBodyReader();
        reader.ReadBool();
        return CountChanges(ref reader);
    }

    private static int CountChanges(ref Reader reader)
    {
        int count = 0;

        ArrayEnd end = reader.ReadArrayStart(DBusType.Struct);
        while (reader.HasNext(end))
        {
            reader.ReadString(); // type: symlink or unlink
            reader.ReadString(); // file name
            reader.ReadString(); // destination
            count++;
        }

        return count;
    }

    private static (string JobPath, string Result) ReadJobRemoved(Message message, object? state)
    {
        Reader reader = message.GetBodyReader();
        reader.ReadUInt32(); // job id
        string jobPath = reader.ReadObjectPathAsString();
        reader.ReadString(); // unit name
        string result = reader.ReadString();
        return (jobPath, result);
    }

    private static string ReadUnitName(Message message, object? state)
    {
        Reader reader = message.GetBodyReader();
        return reader.ReadString();
    }

    private static bool ReadReloading(Message message, object? state)
    {
        Reader reader = message.GetBodyReader();
        return reader.ReadBool();
    }

    /// <summary>
    /// systemd frequently sends PropertiesChanged with an empty body, so only the object path the
    /// signal came from is of interest; the values are re-read afterwards.
    /// </summary>
    private static string ReadChangedObjectPath(Message message, object? state) => message.PathAsString ?? string.Empty;

    private static string ReadString(Dictionary<string, VariantValue> properties, string key) =>
        properties.TryGetValue(key, out VariantValue value) ? value.GetString() : string.Empty;

    private static bool ReadBool(Dictionary<string, VariantValue> properties, string key) =>
        properties.TryGetValue(key, out VariantValue value) && value.GetBool();

    private static uint ReadUInt32(Dictionary<string, VariantValue> properties, string key) =>
        properties.TryGetValue(key, out VariantValue value) ? value.GetUInt32() : 0u;

    /// <summary>
    /// The next time a timer elapses. systemd reports a calendar deadline and a deadline on the
    /// monotonic clock separately; the monotonic one is moved onto the wall clock the same way
    /// <c>systemctl list-timers</c> does it, and the earlier of the two wins.
    /// </summary>
    private static DateTimeOffset? ReadNextElapse(Dictionary<string, VariantValue> timer)
    {
        DateTimeOffset? realtime = ReadTimestamp(timer, "NextElapseUSecRealtime");
        DateTimeOffset? monotonic = null;

        if (timer.TryGetValue("NextElapseUSecMonotonic", out VariantValue value))
        {
            ulong microseconds = value.GetUInt64();

            if (microseconds != 0 && microseconds != ulong.MaxValue)
            {
                // Stopwatch reads CLOCK_MONOTONIC on Linux, the clock systemd's value counts on.
                double nowMicroseconds = Stopwatch.GetTimestamp() * (1_000_000.0 / Stopwatch.Frequency);
                monotonic = DateTimeOffset.UtcNow.AddTicks((long)((microseconds - nowMicroseconds) * 10));
            }
        }

        if (realtime is null)
        {
            return monotonic;
        }

        return monotonic is null || realtime < monotonic ? realtime : monotonic;
    }

    /// <summary>Reads a systemd timestamp, which counts microseconds since the epoch. 0 means never.</summary>
    private static DateTimeOffset? ReadTimestamp(Dictionary<string, VariantValue> properties, string key)
    {
        if (!properties.TryGetValue(key, out VariantValue value))
        {
            return null;
        }

        ulong microseconds = value.GetUInt64();

        if (microseconds == 0 || microseconds == ulong.MaxValue)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeMilliseconds((long)(microseconds / 1000));
    }
}
