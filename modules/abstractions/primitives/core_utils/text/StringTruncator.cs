namespace JoinCode.Abstractions.Utils.Text;

public static class StringTruncator {
    /// <summary>截断字符串到指定长度，使用默认省略号。</summary>
    /// <param name="text">源文本。</param>
    /// <param name="maxLength">最大长度。</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Truncate(string text, int maxLength) {
        return Truncate(text, maxLength, "...", suffixWithinLimit: true);
    }

    /// <summary>截断字符串到指定长度，使用自定义后缀。</summary>
    /// <param name="text">源文本。</param>
    /// <param name="maxLength">最大长度。</param>
    /// <param name="suffix">截断后缀。</param>
    /// <param name="suffixWithinLimit">后缀是否计入长度限制。</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Truncate(string text, int maxLength, string suffix, bool suffixWithinLimit = true) {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength) return text;
        if (suffixWithinLimit) {
            var suffixLen = suffix.Length;
            if (maxLength <= suffixLen) return suffix;
            return string.Concat(text.AsSpan(0, maxLength - suffixLen), suffix);
        }
        return string.Concat(text.AsSpan(0, maxLength), suffix);
    }

    /// <summary>截断字符串中间部分，使用默认省略号。</summary>
    /// <param name="text">源文本。</param>
    /// <param name="maxLength">最大长度。</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string TruncateMiddle(string text, int maxLength) {
        return TruncateMiddle(text, maxLength, "...");
    }

    /// <summary>截断字符串中间部分，使用自定义省略号。</summary>
    /// <param name="text">源文本。</param>
    /// <param name="maxLength">最大长度。</param>
    /// <param name="ellipsis">省略号文本。</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string TruncateMiddle(string text, int maxLength, string ellipsis) {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength) return text;
        var ellipsisLen = ellipsis.Length;
        if (maxLength <= ellipsisLen) return ellipsis;
        var half = (maxLength - ellipsisLen) / 2;
        return string.Concat(text.AsSpan(0, half), ellipsis, text.AsSpan(text.Length - half));
    }

    /// <summary>统计文本行数。</summary>
    /// <param name="text">源文本。</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CountLines(ReadOnlySpan<char> text) {
        if (text.IsEmpty) return 0;
        var count = 0;
        foreach (var c in text)
            if (c == '\n') count++;
        return count;
    }
}
