using System.Globalization;
using System.Text;

namespace LoupixDeck.Plugin.Systemd;

/// <summary>
/// Escapes and unescapes the object path systemd gives a unit, for example
/// <c>/org/freedesktop/systemd1/unit/getty_40tty1_2eservice</c> for <c>getty@tty1.service</c>.
/// <para>
/// The manager returns the path itself for every unit it knows, so escaping is only needed as a
/// fallback, and unescaping only when a signal arrives for a path the registry has not seen yet.
/// </para>
/// </summary>
internal static class SystemdUnitPath
{
    public const string UnitPathPrefix = "/org/freedesktop/systemd1/unit/";

    /// <summary>
    /// Builds the object path of a unit. Every character outside <c>[A-Za-z0-9]</c> becomes an
    /// underscore followed by two lowercase hex digits; a leading digit is escaped as well.
    /// </summary>
    public static string Escape(string unitName)
    {
        if (string.IsNullOrEmpty(unitName))
        {
            return string.Empty;
        }

        StringBuilder builder = new(UnitPathPrefix.Length + (unitName.Length * 3));
        builder.Append(UnitPathPrefix);

        for (int index = 0; index < unitName.Length; index++)
        {
            char character = unitName[index];
            bool isPlain = char.IsAsciiLetterOrDigit(character);

            // A path element may not start with a digit, so the first character is escaped too.
            if (isPlain && !(index == 0 && char.IsAsciiDigit(character)))
            {
                builder.Append(character);
                continue;
            }

            builder.Append('_');
            builder.Append(((byte)character).ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Recovers the unit name from an object path. Returns an empty string when the path does not
    /// belong to a unit.
    /// </summary>
    public static string Unescape(string objectPath)
    {
        if (string.IsNullOrEmpty(objectPath) || !objectPath.StartsWith(UnitPathPrefix, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        string escaped = objectPath[UnitPathPrefix.Length..];
        StringBuilder builder = new(escaped.Length);

        for (int index = 0; index < escaped.Length; index++)
        {
            char character = escaped[index];

            if (character != '_')
            {
                builder.Append(character);
                continue;
            }

            if (index + 2 >= escaped.Length ||
                !byte.TryParse(escaped.AsSpan(index + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte value))
            {
                // Not an escape sequence after all; keep the character so nothing is silently lost.
                builder.Append(character);
                continue;
            }

            builder.Append((char)value);
            index += 2;
        }

        return builder.ToString();
    }
}
