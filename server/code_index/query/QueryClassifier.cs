namespace JoinCode.CodeIndex.Query;

/// <summary>
/// 查询分类器 — 判断查询是符号型、语义型还是混合型。
/// <para>符号型：包含标识符、"who calls"、"find references" 等精确查找。</para>
/// <para>语义型：自然语言描述、"find similar"、"code that does X" 等。</para>
/// <para>混合型：同时包含符号名和自然语言描述。</para>
/// </summary>
public sealed class QueryClassifier {

    private static readonly FrozenSet<string> SymbolKeywords = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "who calls", "who uses", "find references", "find usages",
        "callers of", "callees of", "definition of", "goto definition",
        "find symbol", "where is", "implementations of");

    private static readonly FrozenSet<string> SemanticKeywords = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "find similar", "code that", "function that", "method that",
        "how to", "where do we", "pattern like", "analogous to",
        "refactor", "optimize", "explain");

    /// <summary>
    /// 分类查询类型。
    /// </summary>
    /// <param name="query">用户查询文本。</param>
    /// <returns>分类结果：Symbol/Semantic/Hybrid。</returns>
    public QueryKind Classify(string query) {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query)) return QueryKind.Semantic;

        var hasSymbolKeyword = ContainsAnyKeyword(query, SymbolKeywords);
        var hasSemanticKeyword = ContainsAnyKeyword(query, SemanticKeywords);

        if (hasSymbolKeyword && hasSemanticKeyword) return QueryKind.Hybrid;
        if (hasSymbolKeyword) return QueryKind.Symbol;
        if (hasSemanticKeyword) return QueryKind.Semantic;

        var looksLikeId = LooksLikeIdentifier(query);
        var looksLikeNL = LooksLikeNaturalLanguage(query);

        return (looksLikeId, looksLikeNL) switch {
            (true, true) => QueryKind.Hybrid,
            (true, false) => QueryKind.Symbol,
            (false, true) => QueryKind.Semantic,
            _ => QueryKind.Semantic
        };
    }

    private static bool ContainsAnyKeyword(string query, FrozenSet<string> keywords) {
        foreach (var keyword in keywords) {
            if (query.Contains(keyword, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 判断是否像标识符 — 包含点分隔的名称（如 Foo.Bar.Baz）或 PascalCase 单词。
    /// </summary>
    private static bool LooksLikeIdentifier(string query) {
        var trimmed = query.Trim();
        if (trimmed.Contains('.')) return true;
        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 1 && words[0].Length > 1) {
            var firstChar = words[0][0];
            return char.IsUpper(firstChar) || firstChar == '_';
        }
        return false;
    }

    /// <summary>
    /// 判断是否像自然语言 — 包含空格分隔的多个词且非标识符模式。
    /// </summary>
    private static bool LooksLikeNaturalLanguage(string query) {
        var trimmed = query.Trim();
        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length >= 3;
    }
}
