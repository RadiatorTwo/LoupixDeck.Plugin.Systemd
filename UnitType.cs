namespace LoupixDeck.Plugin.Systemd;

/// <summary>
/// The unit types the selector can list. A type is stored as systemd's own suffix without the dot,
/// for example <c>service</c>, so the stored value reads the same as a unit name ends.
/// </summary>
internal static class UnitTypeParser
{
    public const string ServiceValue = "service";
    public const string SocketValue = "socket";
    public const string TimerValue = "timer";
    public const string MountValue = "mount";
    public const string TargetValue = "target";

    /// <summary>Every type the plugin offers, in the order the menu lists them.</summary>
    public static readonly IReadOnlyList<string> Supported =
        [ServiceValue, SocketValue, TimerValue, MountValue, TargetValue];

    /// <summary>The tokens a setting description lists.</summary>
    public const string SupportedValues =
        ServiceValue + ", " + SocketValue + ", " + TimerValue + ", " + MountValue + ", " + TargetValue;

    /// <summary>
    /// Reads the stored entries. A hand-typed ".timer" or "*.timer" means the same as "timer";
    /// anything the plugin does not know is dropped. When nothing usable is left, the list falls
    /// back to services, which is what the plugin listed before the setting took effect.
    /// </summary>
    public static IReadOnlyList<string> Parse(IEnumerable<string> entries)
    {
        HashSet<string> requested = new(StringComparer.Ordinal);

        foreach (string entry in entries)
        {
            requested.Add(entry.Trim().TrimStart('*').TrimStart('.').ToLowerInvariant());
        }

        List<string> types = [.. Supported.Where(requested.Contains)];
        return types.Count == 0 ? [ServiceValue] : types;
    }

    /// <summary>The type of a unit, read from its suffix, or empty when the name has none.</summary>
    public static string Of(string unitName)
    {
        int separator = unitName.LastIndexOf('.');
        return separator > 0 ? unitName[(separator + 1)..] : string.Empty;
    }

    /// <summary>The name patterns ListUnitsByPatterns takes for these types.</summary>
    public static IReadOnlyList<string> ToPatterns(IReadOnlyList<string> types) =>
        [.. types.Select(type => "*." + type)];

    /// <summary>The English label a translated menu group is built from.</summary>
    public static string ToEnglishText(string type)
    {
        return type switch
        {
            SocketValue => "Sockets",
            TimerValue => "Timers",
            MountValue => "Mounts",
            TargetValue => "Targets",
            _ => "Services"
        };
    }

    /// <summary>
    /// The unit name as a button shows it. A service drops its suffix, as it always did; any other
    /// type keeps it, because "foo" alone would not say whether the socket or the service is meant.
    /// </summary>
    public static string ShortName(string unitName)
    {
        const string serviceSuffix = "." + ServiceValue;

        return unitName.Length > serviceSuffix.Length && unitName.EndsWith(serviceSuffix, StringComparison.Ordinal)
            ? unitName[..^serviceSuffix.Length]
            : unitName;
    }
}
