namespace NativeTui.Core.Repl;

/// <summary>
/// 权限对话框文本折行（S8）— 命令全文等宽展示的词边界折行纯函数：
/// 显式换行符先拆段，各段按宽度 word-wrap（空格分词 + 单词超宽硬切）。
/// 消费方：ToolPermissionForms 命令区折行（MeasureHeight 与 Render 同参数保证行数一致）。
/// </summary>
public static class PermissionTextWrap
{
    /// <summary>
    /// 折行文本 — 显式 '\n' 拆段后各段 word-wrap；空文本返回空数组。
    /// width &lt;= 0 时防御性单行原样返回（调用方宽度契约破坏时不丢内容）。
    /// </summary>
    /// <param name="text">待折行文本（可含显式换行符）。</param>
    /// <param name="width">目标行宽（显示列数）。</param>
    /// <returns>折行后的行集（无尾随空格）。</returns>
    public static string[] Wrap(string text, int width)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        if (width <= 0)
        {
            return [text];
        }

        return text.Split('\n')
            .SelectMany(segment => WrapSegment(segment, width))
            .ToArray();
    }

    /// <summary>单段 word-wrap — 空格分词逐词拼接，当前行放不下则换行；单词本身超宽按宽度硬切。</summary>
    private static IEnumerable<string> WrapSegment(string segment, int width)
    {
        var lines = new List<string>();
        var current = new StringBuilder();
        foreach (var word in segment.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            AppendWord(lines, current, word, width);
        }

        if (current.Length > 0 || lines.Count == 0)
        {
            lines.Add(current.ToString());
        }

        return lines;
    }

    /// <summary>追加单词 — 能放下则拼接（带间隔空格），放不下则当前行落盘开新行；超宽单词硬切片。</summary>
    private static void AppendWord(List<string> lines, StringBuilder current, string word, int width)
    {
        if (word.Length > width)
        {
            FlushCurrent(lines, current);
            lines.AddRange(ChunkWord(word, width));
            return;
        }

        var needed = current.Length == 0 ? word.Length : current.Length + 1 + word.Length;
        if (needed > width)
        {
            FlushCurrent(lines, current);
        }
        else if (current.Length > 0)
        {
            current.Append(' ');
        }

        current.Append(word);
    }

    /// <summary>当前行落盘（非空时）并清空缓冲。</summary>
    private static void FlushCurrent(List<string> lines, StringBuilder current)
    {
        if (current.Length > 0)
        {
            lines.Add(current.ToString());
            current.Clear();
        }
    }

    /// <summary>超宽单词硬切 — 按 width 等宽切片。</summary>
    private static IEnumerable<string> ChunkWord(string word, int width)
    {
        for (var offset = 0; offset < word.Length; offset += width)
        {
            yield return word.Substring(offset, Math.Min(width, word.Length - offset));
        }
    }
}
