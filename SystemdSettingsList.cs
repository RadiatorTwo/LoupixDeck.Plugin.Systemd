namespace LoupixDeck.Plugin.Systemd;

/// <summary>
/// Reads and writes the list settings. The SDK has no list editor, so a list is a single text
/// field; commas, semicolons and line breaks all separate entries, because a user editing the
/// field by hand should not have to guess which one counts.
/// </summary>
internal static class SystemdSettingsList
{
    private static readonly char[] Separators = [',', ';', '\n', '\r'];

    /// <summary>Splits a stored value into its entries, dropping blanks and duplicates.</summary>
    public static IReadOnlyList<string> Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        List<string> entries = [];

        foreach (string part in value.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!entries.Contains(part, StringComparer.Ordinal))
            {
                entries.Add(part);
            }
        }

        return entries;
    }

    /// <summary>Formats entries back into the stored value.</summary>
    public static string Format(IEnumerable<string> entries) => string.Join(", ", entries);
}
