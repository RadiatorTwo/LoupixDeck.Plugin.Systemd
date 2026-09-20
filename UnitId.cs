namespace LoupixDeck.Plugin.Systemd;

/// <summary>
/// Identifies one unit in one systemd instance, for example <c>user:pipewire.service</c>. This is
/// the value a button stores as its command parameter and the value the favorites list holds, so
/// its text form is a stable part of the plugin's public surface.
/// </summary>
public readonly record struct UnitId(UnitDomain Domain, string Name)
{
    private const char Separator = ':';

    /// <summary>True when the id carries a usable unit name.</summary>
    public bool IsValid => !string.IsNullOrWhiteSpace(Name);

    /// <summary>
    /// Parses <c>domain:name</c>. A value without a domain prefix is read as a bare unit name and
    /// placed in <paramref name="fallbackDomain"/>, so a hand-typed "sshd.service" still works.
    /// </summary>
    public static UnitId Parse(string? value, UnitDomain fallbackDomain = UnitDomain.User)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new UnitId(fallbackDomain, string.Empty);
        }

        string trimmed = value.Trim();
        int separator = trimmed.IndexOf(Separator);

        if (separator < 0)
        {
            return new UnitId(fallbackDomain, Normalize(trimmed));
        }

        UnitDomain domain = UnitDomainParser.Parse(trimmed[..separator]);
        return new UnitId(domain, Normalize(trimmed[(separator + 1)..]));
    }

    /// <summary>The text form stored in the config. Unit names are case-sensitive, so it is kept verbatim.</summary>
    public override string ToString() =>
        string.Concat(UnitDomainParser.ToParameterValue(Domain), Separator.ToString(), Name);

    /// <summary>
    /// A unit name without a type suffix is assumed to be a service, which is the only unit type
    /// this version offers. That keeps a hand-typed "sshd" working.
    /// </summary>
    private static string Normalize(string name)
    {
        string trimmed = name.Trim();

        if (trimmed.Length == 0 || trimmed.Contains('.'))
        {
            return trimmed;
        }

        return trimmed + ".service";
    }
}
