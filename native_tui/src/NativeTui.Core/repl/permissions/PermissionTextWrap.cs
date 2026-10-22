namespace NativeTui.Core.Repl;

/// <summary>
/// 权限对话框文本折行（S8）— 命令全文等宽展示的词边界折行纯函数：
/// 显式换行符先拆段，各段按显示列宽 word-wrap（空格分词 + 单词超宽硬切 — 复用
/// Grid.RuneWidth East Asian Width 判定，CJK/全角/emoji 2 列、组合符 0 列，ASCII 恒 1 列）。
/// 消费方：ToolPermissionForms 命令区折行（MeasureHeight 与 Render 同参数保证行数一致）
/// + BackgroundTaskDetailDialog 描述折行。
/// </summary>
public static class PermissionTextWrap
{
    /// <summary>
    /// 折行文本 — 显式 '\n' 拆段后各段按显示列宽 word-wrap；空文本返回空数组。
    /// width &lt;= 0 时防御性单行原样返回（调用方宽度契约破坏时不丢内容）。
    /// </summary>
    /// <param name="text">待折行文本（可含显式换行符）。</param>
    /// <param name="width">目标行宽（显示列数 — 宽字符占 2 列）。</param>
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

    /// <summary>单段 word-wrap — 空格分词逐词拼接，当前行放不下则换行；单词本身超宽按显示列宽硬切。</summary>
    private static IEnumerable<string> WrapSegment(string segment, int width)
    {
        var lines = new List<string>();
        var current = new LineBuffer();
        foreach (var word in segment.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            AppendWord(lines, current, word, width);
        }

        if (!current.IsEmpty || lines.Count == 0)
        {
            lines.Add(current.ToString());
        }

        return lines;
    }

    /// <summary>追加单词 — 能放下则拼接（带间隔空格），放不下则当前行落盘开新行；超宽单词按显示列宽硬切片。</summary>
    private static void AppendWord(List<string> lines, LineBuffer current, string word, int width)
    {
        var wordWidth = RuneWidth.OfText(word);
        if (wordWidth > width)
        {
            FlushCurrent(lines, current);
            lines.AddRange(ChunkWord(word, width));
            return;
        }

        var needed = current.IsEmpty ? wordWidth : current.Width + 1 + wordWidth;
        if (needed > width)
        {
            FlushCurrent(lines, current);
        }
        else if (!current.IsEmpty)
        {
            current.AppendSeparator();
        }

        current.Append(word, wordWidth);
    }

    /// <summary>当前行落盘（非空时）并清空缓冲。</summary>
    private static void FlushCurrent(List<string> lines, LineBuffer current)
    {
        if (current.Flush() is { } line)
        {
            lines.Add(line);
        }
    }

    /// <summary>超宽单词硬切 — 按显示列宽逐 rune 累计切片（切点取不超 width 的最大字符前缀）；
    /// 行首保底收 1 个 rune（width&lt;2 遇宽字符防空切片死循环 — 超宽尾列由 Screen 截断兜底，不丢内容）；
    /// 零宽 rune（组合符/变体选择器）不占列，跟随基字符同片不被切开。</summary>
    private static IEnumerable<string> ChunkWord(string word, int width)
    {
        var start = 0;
        var column = 0;
        var cursor = 0;
        foreach (var rune in word.EnumerateRunes())
        {
            var runeWidth = RuneWidth.Of(rune);
            if (column > 0 && column + runeWidth > width)
            {
                yield return word.Substring(start, cursor - start);
                start = cursor;
                column = 0;
            }

            column += runeWidth;
            cursor += rune.Utf16SequenceLength;
        }

        if (cursor > start)
        {
            yield return word.Substring(start, cursor - start);
        }
    }

    /// <summary>当前行缓冲 — 文本与显示列宽同步累计（追加即累宽、落盘后归零，免重复测宽遍历）。</summary>
    private sealed class LineBuffer
    {
        private readonly StringBuilder _text = new();

        /// <summary>当前行显示列宽（含词间空格 1 列）。</summary>
        public int Width { get; private set; }

        /// <summary>当前行是否为空（无任何字符）。</summary>
        public bool IsEmpty => _text.Length == 0;

        /// <summary>追加词间间隔空格（占 1 显示列）。</summary>
        public void AppendSeparator()
        {
            _text.Append(' ');
            Width += 1;
        }

        /// <summary>追加词文本（显示列宽由调用方一次测得传入 — 免二次遍历）。</summary>
        public void Append(string text, int textWidth)
        {
            _text.Append(text);
            Width += textWidth;
        }

        /// <summary>落盘当前行并重置（空行返回 null 不落盘）。</summary>
        public string? Flush()
        {
            if (_text.Length == 0)
            {
                return null;
            }

            var line = _text.ToString();
            _text.Clear();
            Width = 0;
            return line;
        }

        /// <summary>当前行文本（空行即空串 — 空段落盘占位）。</summary>
        public override string ToString() => _text.ToString();
    }
}
