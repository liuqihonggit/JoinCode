namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 行号前缀格式化工具 — 统一 FileRead/gh 日志的行号格式。
/// 对齐 TS: addLineNumbers / stripLineNumberPrefix。
/// 紧凑模式(compact=true): 行号 + \t + 内容 — cat -n 风格，LLM 训练数据匹配度高。
/// 箭头模式(compact=false): 行号右对齐到 ≥6 位 + → + 内容 — 视觉直观。
/// </summary>
public static class LineNumberFormatter {
    /// <summary>
    /// 行号箭头分隔符 — U+2192 RIGHTWARDS ARROW。
    /// </summary>
    public const char Arrow = '\u2192';

    /// <summary>
    /// 格式化单行带行号前缀。
    /// 紧凑模式: "行号\t内容"。箭头模式: "行号→内容"(行号 &lt; 6位时右对齐到6位)。
    /// 对齐 TS: addLineNumbers 单行逻辑 — 流式日志场景(不知道总行数)用固定6位填充。
    /// </summary>
    /// <param name="lineNumber">1-based 行号。</param>
    /// <param name="content">行内容(已去时间戳等噪音)。</param>
    /// <param name="compact">true=紧凑 tab 格式，false=箭头 → 格式。</param>
    /// <returns>带行号前缀的行。</returns>
    public static string Format(int lineNumber, string content, bool compact) {
        if (compact) {
            return $"{lineNumber}\t{content}";
        }
        var numStr = lineNumber.ToString();
        if (numStr.Length >= 6) {
            return $"{numStr}{Arrow}{content}";
        }
        return $"{numStr.PadLeft(6)}{Arrow}{content}";
    }

    /// <summary>
    /// 格式化多行带行号前缀。
    /// 紧凑模式: 每行 "行号\t内容"。箭头模式: 每行 "行号→内容"(右对齐到 max(最大行号位数, 6))。
    /// 对齐 TS: addLineNumbers 多行逻辑 — FileRead 场景(知道总行数)用动态 padWidth。
    /// </summary>
    /// <param name="content">多行内容(\n 分隔)。</param>
    /// <param name="startLine">第一行的 1-based 行号。</param>
    /// <param name="compact">true=紧凑 tab 格式，false=箭头 → 格式。</param>
    /// <returns>每行带行号前缀的多行文本。</returns>
    public static string FormatMultiLine(string content, int startLine, bool compact) {
        if (string.IsNullOrEmpty(content)) {
            return string.Empty;
        }

        var lines = content.Split(['\n'], StringSplitOptions.None);

        if (compact) {
            var compactSb = new StringBuilder(content.Length + lines.Length * 4);
            for (var i = 0; i < lines.Length; i++) {
                compactSb.Append(startLine + i);
                compactSb.Append('\t');
                compactSb.AppendLine(lines[i]);
            }

            return compactSb.ToString();
        }

        var maxLineNum = startLine + lines.Length - 1;
        var maxDigits = maxLineNum.ToString().Length;
        var padWidth = Math.Max(maxDigits, 6);

        var sb = new StringBuilder(content.Length + lines.Length * (padWidth + 2));
        for (var i = 0; i < lines.Length; i++) {
            var lineNum = startLine + i;
            sb.Append(lineNum.ToString().PadLeft(padWidth));
            sb.Append(Arrow);
            sb.AppendLine(lines[i]);
        }

        return sb.ToString();
    }
}
