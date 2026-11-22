namespace JoinCode.Abstractions.Security.Shell;

public sealed partial class BashAstSecurityWalker {
    private static BashAstSecurityResult? RunPreChecks(string command) {
        foreach (var item in BashPreCheckRegistry.All) {
            if (item.IsMatch(command))
                return new BashAstSecurityResult.TooComplex(item.Message, item.NodeType);
        }

        var masked = MaskBracesInQuotedContexts(command);
        if (BashSecurityRegex.BraceWithQuoteRegex().IsMatch(masked))
            return new BashAstSecurityResult.TooComplex("命令包含花括号+引号混淆", "BRACE_WITH_QUOTE");

        return null;
    }

    private static string MaskBracesInQuotedContexts(string cmd) {
        if (!cmd.Contains('{')) return cmd;

        var result = new StringBuilder(cmd.Length);
        var inSingle = false;
        var inDouble = false;

        for (var i = 0; i < cmd.Length; i++) {
            var c = cmd[i];

            if (inSingle) {
                if (c == '\'') inSingle = false;
                result.Append(c == '{' ? ' ' : c);
            } else if (inDouble) {
                if (c == '\\' && i + 1 < cmd.Length && (cmd[i + 1] == '"' || cmd[i + 1] == '\\')) {
                    result.Append(c);
                    result.Append(cmd[i + 1]);
                    i++;
                } else {
                    inDouble = c != '"';
                    result.Append(c == '{' ? ' ' : c);
                }
            } else {
                if (c == '\\' && i + 1 < cmd.Length) {
                    result.Append(c);
                    result.Append(cmd[i + 1]);
                    i++;
                } else {
                    inSingle = c == '\'';
                    inDouble = c == '"';
                    result.Append(c);
                }
            }
        }

        return result.ToString();
    }

    private static bool HasErrorNode(Node node) {
        if (node.IsError || node.IsMissing) return true;
        foreach (var child in node.Children) {
            if (HasErrorNode(child)) return true;
        }
        return false;
    }

    private static BashAstSecurityResult TooComplex(Node node)
        => new BashAstSecurityResult.TooComplex($"无法静态分析: {node.Type}", node.Type);

    private static BashAstSecurityResult TooComplexNode(Node node)
        => new BashAstSecurityResult.TooComplex($"无法静态分析: {node.Type}", node.Type);

    private static string StripRawString(string text) {
        if (text.Length >= 2 && text[0] == '\'' && text[^1] == '\'')
            return text[1..^1];
        return text;
    }

    private static bool ContainsAnyPlaceholder(string value)
        => value.Contains(CmdsubPlaceholder, StringComparison.Ordinal) ||
           value.Contains(VarPlaceholder, StringComparison.Ordinal);

    private static bool IsValidVarName(string name)
        => BashSecurityRegex.ValidVarNameRegex().IsMatch(name);

    private static bool IsPs4ValueSafe(string value) {
        var stripped = BashSecurityRegex.Ps4VarRefRegex().Replace(value, "");
        return BashSecurityRegex.Ps4SafeCharsetRegex().IsMatch(stripped);
    }

    private sealed class StringOrTooComplex {
        /// <summary>获取字符串值。</summary>
        public string Value { get; }
        /// <summary>获取过于复杂的结果（为 null 表示非复杂）。</summary>
        public BashAstSecurityResult? TooComplex { get; }
        /// <summary>获取是否过于复杂。</summary>
        public bool IsTooComplex => TooComplex is not null;

        /// <summary>构造字符串结果。</summary>
        /// <param name="value">字符串值。</param>
        public StringOrTooComplex(string value) { Value = value; TooComplex = null; }
        /// <summary>构造过于复杂的结果。</summary>
        /// <param name="tooComplex">过于复杂的安全结果。</param>
        public StringOrTooComplex(BashAstSecurityResult tooComplex) { Value = ""; TooComplex = tooComplex; }

        /// <summary>获取过于复杂的结果，若不存在则抛出异常。</summary>
        public BashAstSecurityResult GetTooComplex() =>
            TooComplex ?? throw new InvalidOperationException("TooComplex is null when IsTooComplex is false.");
    }

    private sealed record VarAssignmentResult(string Name, string Value, bool IsAppend);

    private sealed class VarAssignmentOrTooComplex {
        /// <summary>获取变量赋值结果（为 null 表示过于复杂）。</summary>
        public VarAssignmentResult? Result { get; }
        /// <summary>获取过于复杂的结果（为 null 表示非复杂）。</summary>
        public BashAstSecurityResult? TooComplex { get; }
        /// <summary>获取是否过于复杂。</summary>
        public bool IsTooComplex => TooComplex is not null;

        /// <summary>构造变量赋值结果。</summary>
        /// <param name="result">变量赋值结果。</param>
        public VarAssignmentOrTooComplex(VarAssignmentResult result) { Result = result; TooComplex = null; }
        /// <summary>构造过于复杂的结果。</summary>
        /// <param name="tooComplex">过于复杂的安全结果。</param>
        public VarAssignmentOrTooComplex(BashAstSecurityResult tooComplex) { Result = null; TooComplex = tooComplex; }

        /// <summary>获取变量赋值结果，若不存在则抛出异常。</summary>
        public VarAssignmentResult GetResult() =>
            Result ?? throw new InvalidOperationException("Result is null when IsTooComplex is true.");
    }

    private sealed record RedirectResult(string Op, string Target);

    private sealed class RedirectOrTooComplex {
        /// <summary>获取重定向结果（为 null 表示过于复杂）。</summary>
        public RedirectResult? Result { get; }
        /// <summary>获取过于复杂的结果（为 null 表示非复杂）。</summary>
        public BashAstSecurityResult? TooComplex { get; }
        /// <summary>获取是否过于复杂。</summary>
        public bool IsTooComplex => TooComplex is not null;

        /// <summary>构造重定向结果。</summary>
        /// <param name="result">重定向结果。</param>
        public RedirectOrTooComplex(RedirectResult result) { Result = result; TooComplex = null; }
        /// <summary>构造过于复杂的结果。</summary>
        /// <param name="tooComplex">过于复杂的安全结果。</param>
        public RedirectOrTooComplex(BashAstSecurityResult tooComplex) { Result = null; TooComplex = tooComplex; }

        /// <summary>获取重定向结果，若不存在则抛出异常。</summary>
        public RedirectResult GetResult() =>
            Result ?? throw new InvalidOperationException("Result is null when IsTooComplex is true.");
    }
}