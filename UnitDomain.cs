namespace LoupixDeck.Plugin.Systemd;

/// <summary>
/// The systemd instance a unit belongs to. The user instance lives on the session bus, the
/// system instance on the system bus; the same unit name can exist in both.
/// </summary>
public enum UnitDomain
{
    User,
    System
}

/// <summary>Converts a <see cref="UnitDomain"/> to and from the token stored in the config.</summary>
internal static class UnitDomainParser
{
    public const string UserValue = "user";
    public const string SystemValue = "system";

    /// <summary>Parses a stored token. Anything unknown falls back to the user instance.</summary>
    public static UnitDomain Parse(string? value)
    {
        return string.Equals(value?.Trim(), SystemValue, StringComparison.OrdinalIgnoreCase)
            ? UnitDomain.System
            : UnitDomain.User;
    }

    /// <summary>The token persisted in the config and in a unit id. Never change these.</summary>
    public static string ToParameterValue(UnitDomain domain) =>
        domain == UnitDomain.System ? SystemValue : UserValue;

    /// <summary>The English label a translated display string is built from.</summary>
    public static string ToEnglishText(UnitDomain domain) =>
        domain == UnitDomain.System ? "System" : "User";
}
