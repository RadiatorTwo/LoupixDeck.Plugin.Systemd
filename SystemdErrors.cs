using Tmds.DBus.Protocol;

namespace LoupixDeck.Plugin.Systemd;

/// <summary>How a systemd call ended. Anything but <see cref="Ok"/> is shown to the user.</summary>
public enum UnitCallOutcome
{
    Ok,

    /// <summary>PolicyKit refused the call. The unit itself is untouched and must not look failed.</summary>
    PermissionDenied,

    /// <summary>systemd does not know this unit.</summary>
    NotFound,

    /// <summary>The bus or the manager is not reachable right now.</summary>
    Unavailable,

    /// <summary>The call reached systemd and was rejected for another reason.</summary>
    Failed
}

/// <summary>Maps a D-Bus error onto a <see cref="UnitCallOutcome"/>.</summary>
internal static class SystemdErrors
{
    public const string AccessDenied = "org.freedesktop.DBus.Error.AccessDenied";
    public const string InteractiveAuthorizationRequired = "org.freedesktop.DBus.Error.InteractiveAuthorizationRequired";
    public const string PolicyKitNotAuthorized = "org.freedesktop.PolicyKit1.Error.NotAuthorized";
    /// <summary>
    /// systemd's answer to a second Subscribe on a connection that already has one. It survives a
    /// daemon-reexec, so the manager reports it although the plugin has just been told the manager
    /// is new. The subscription is in place either way, which is all the caller wanted.
    /// </summary>
    public const string AlreadySubscribed = "org.freedesktop.systemd1.AlreadySubscribed";

    public const string NoSuchUnit = "org.freedesktop.systemd1.NoSuchUnit";
    public const string LoadFailed = "org.freedesktop.systemd1.LoadFailed";
    public const string UnknownObject = "org.freedesktop.DBus.Error.UnknownObject";
    public const string UnknownMethod = "org.freedesktop.DBus.Error.UnknownMethod";
    public const string ServiceUnknown = "org.freedesktop.DBus.Error.ServiceUnknown";
    public const string NoReply = "org.freedesktop.DBus.Error.NoReply";
    public const string Disconnected = "org.freedesktop.DBus.Error.Disconnected";

    /// <summary>Classifies an exception raised by a D-Bus call.</summary>
    public static UnitCallOutcome Map(Exception? exception)
    {
        return exception switch
        {
            null => UnitCallOutcome.Ok,
            DBusErrorReplyException reply => Map(reply.ErrorName),
            TimeoutException => UnitCallOutcome.Unavailable,
            OperationCanceledException => UnitCallOutcome.Unavailable,
            ObjectDisposedException => UnitCallOutcome.Unavailable,
            _ => UnitCallOutcome.Failed
        };
    }

    /// <summary>Classifies a D-Bus error name.</summary>
    public static UnitCallOutcome Map(string? errorName)
    {
        return errorName switch
        {
            null or "" => UnitCallOutcome.Failed,
            AlreadySubscribed => UnitCallOutcome.Ok,
            AccessDenied or InteractiveAuthorizationRequired or PolicyKitNotAuthorized => UnitCallOutcome.PermissionDenied,
            NoSuchUnit or LoadFailed or UnknownObject or UnknownMethod => UnitCallOutcome.NotFound,
            ServiceUnknown or NoReply or Disconnected => UnitCallOutcome.Unavailable,
            _ => UnitCallOutcome.Failed
        };
    }

    /// <summary>The English text a button or the log shows for an outcome.</summary>
    public static string ToEnglishText(UnitCallOutcome outcome)
    {
        return outcome switch
        {
            UnitCallOutcome.Ok => "Done",
            UnitCallOutcome.PermissionDenied => "No permission",
            UnitCallOutcome.NotFound => "Not found",
            UnitCallOutcome.Unavailable => "Unavailable",
            _ => "Failed"
        };
    }
}
