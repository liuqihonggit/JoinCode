namespace Core.Hooks.Execution.Interception.Defense;

/// <summary>
/// 严格解析检测 node — 独立公共对象，检测命令中未闭合的引号。
/// <para>
/// MTP 扰动纵深防御约束第2条：结构化解析是承重墙，解析失败（引号不配对）直接拒绝，不进入下游。
/// MTP 扰动可能丢字符导致引号不配对，如 <c>echo "hello >nul</c>（丢了一个引号）。
/// </para>
/// </summary>
[Register(typeof(StrictParseNode), ServiceLifetime.Singleton)]
public sealed class StrictParseNode {
    /// <summary>
    /// 检测命令中是否有未闭合的引号。
    /// </summary>
    /// <param name="command">命令字符串</param>
    /// <returns>未闭合的引号字符（' 或 "）；null 表示引号配对正常</returns>
    public char? FindUnmatchedQuote(ReadOnlySpan<char> command) {
        var inSingleQuote = false;
        var inDoubleQuote = false;

        foreach (var c in command) {
            if (c == '\'' && !inDoubleQuote)
                inSingleQuote = !inSingleQuote;
            else if (c == '"' && !inSingleQuote)
                inDoubleQuote = !inDoubleQuote;
        }

        if (inSingleQuote)
            return '\'';
        if (inDoubleQuote)
            return '"';
        return null;
    }
}