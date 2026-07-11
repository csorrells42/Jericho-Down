using System.Globalization;

namespace JerichoDown.Audio;

public static class MidiHexParser
{
    public static bool TryParseBytes(string? text, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text
            .Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Trim();
        if (normalized.Length == 0 || normalized.Length % 2 != 0)
        {
            return false;
        }

        var parsed = new byte[normalized.Length / 2];
        for (var i = 0; i < parsed.Length; i++)
        {
            if (!byte.TryParse(normalized.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed[i]))
            {
                bytes = [];
                return false;
            }
        }

        bytes = parsed;
        return true;
    }

    public static bool TryParseShortMessage(string? text, out int rawMessage)
    {
        rawMessage = 0;
        if (!TryParseBytes(text, out var bytes) || bytes.Length is < 1 or > 3)
        {
            return false;
        }

        for (var i = 0; i < bytes.Length; i++)
        {
            rawMessage |= bytes[i] << (8 * i);
        }

        return true;
    }

    public static string ToHex(byte[] bytes)
    {
        return string.Join(" ", bytes.Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));
    }
}
