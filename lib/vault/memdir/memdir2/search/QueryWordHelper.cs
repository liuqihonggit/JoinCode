namespace Core.Memdir;

internal static class QueryWordHelper
{
    private static readonly SearchValues<char> WordSeparators =
        SearchValues.Create(" \t\n\r.,!?;:()[]{}");

    internal static string[] ExtractQueryWords(ReadOnlySpan<char> query)
    {
        if (query.IsEmpty || query.IsWhiteSpace())
        {
            return [];
        }

        var words = new List<string>();
        while (!query.IsEmpty)
        {
            var idx = query.IndexOfAny(WordSeparators);
            var token = idx < 0 ? query : query[..idx];

            if (!token.IsEmpty)
            {
                words.Add(token.ToString());
            }

            query = idx < 0 ? [] : query[(idx + 1)..];
        }

        return words.ToArray();
    }

    internal static HashSet<string> ExtractWords(string text, int minLength = 0)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var span = text.AsSpan();
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (!span.IsEmpty)
        {
            var idx = span.IndexOfAny(WordSeparators);
            var token = idx < 0 ? span : span[..idx];

            if (!token.IsEmpty && token.Length > minLength)
            {
                words.Add(token.ToString());
            }

            span = idx < 0 ? [] : span[(idx + 1)..];
        }

        return words;
    }

    internal static bool ContainsOrdinalIgnoreCase(ReadOnlySpan<char> source, ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
        {
            return true;
        }

        if (source.IsEmpty)
        {
            return false;
        }

        for (var i = 0; i <= source.Length - value.Length; i++)
        {
            if (source.Slice(i, value.Length).Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool ContainsWholeWordOrdinalIgnoreCase(ReadOnlySpan<char> source, ReadOnlySpan<char> word)
    {
        if (word.IsEmpty || source.IsEmpty || word.Length > source.Length)
        {
            return false;
        }

        if (source.Length >= word.Length &&
            source.Slice(0, word.Length).Equals(word, StringComparison.OrdinalIgnoreCase) &&
            (source.Length == word.Length || char.IsWhiteSpace(source[word.Length])))
        {
            return true;
        }

        if (source.Length >= word.Length &&
            source.Slice(source.Length - word.Length, word.Length).Equals(word, StringComparison.OrdinalIgnoreCase) &&
            char.IsWhiteSpace(source[source.Length - word.Length - 1]))
        {
            return true;
        }

        for (var i = 1; i <= source.Length - word.Length - 1; i++)
        {
            if (source.Slice(i, word.Length).Equals(word, StringComparison.OrdinalIgnoreCase) &&
                char.IsWhiteSpace(source[i - 1]) &&
                char.IsWhiteSpace(source[i + word.Length]))
            {
                return true;
            }
        }

        return false;
    }
}
