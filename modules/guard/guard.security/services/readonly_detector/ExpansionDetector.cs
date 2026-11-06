namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 扩展检测器实现 — 无独立数据源，纯字符串分析
/// </summary>
internal sealed class ExpansionDetector : IExpansionDetector {
    /// <inheritdoc />
    public bool ContainsUnquotedExpansion(string command) {
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var i = 0; i < command.Length; i++) {
            var c = command[i];

            if (c == '\'' && !inDoubleQuote) {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (c == '"' && !inSingleQuote) {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (inSingleQuote) {
                continue;
            }

            if (c == '$' && i + 1 < command.Length
                && (char.IsLetterOrDigit(command[i + 1]) || command[i + 1] == '_' || command[i + 1] == '{')) {
                return true;
            }

            if (!inDoubleQuote && (c is '?' or '*' || c == '[')) {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public List<string> SplitCommandTokens(string command) {
        var parts = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var quoteChar = '\0';

        for (var i = 0; i < command.Length; i++) {
            var c = command[i];

            if ((c == '"' || c == '\'') && !inQuotes) {
                inQuotes = true;
                quoteChar = c;
                continue;
            }

            if (c == quoteChar && inQuotes) {
                inQuotes = false;
                quoteChar = '\0';
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuotes) {
                if (current.Length > 0) {
                    parts.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0) {
            parts.Add(current.ToString());
        }

        return parts;
    }
}
