namespace JoinCode.Abstractions.Utils.Text;

public static class StringTruncator
{
    public static string Truncate(string text, int maxLength)
    {
        return Truncate(text, maxLength, "...", suffixWithinLimit: true);
    }

    public static string Truncate(string text, int maxLength, string suffix, bool suffixWithinLimit = true)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength) return text;
        if (suffixWithinLimit)
        {
            var suffixLen = suffix.Length;
            if (maxLength <= suffixLen) return suffix;
            return string.Concat(text.AsSpan(0, maxLength - suffixLen), suffix);
        }
        return string.Concat(text.AsSpan(0, maxLength), suffix);
    }

    public static string TruncateMiddle(string text, int maxLength)
    {
        return TruncateMiddle(text, maxLength, "...");
    }

    public static string TruncateMiddle(string text, int maxLength, string ellipsis)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength) return text;
        var ellipsisLen = ellipsis.Length;
        if (maxLength <= ellipsisLen) return ellipsis;
        var half = (maxLength - ellipsisLen) / 2;
        return string.Concat(text.AsSpan(0, half), ellipsis, text.AsSpan(text.Length - half));
    }

    public static int CountLines(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty) return 0;
        var count = 0;
        foreach (var c in text)
            if (c == '\n') count++;
        return count;
    }
}
