namespace NativeTui.Core.Repl.Vim;

/// <summary>
/// vim motion — 基准 src/vim/motions.ts:30-67 applySingleMotion 的 switch 全集
///（h l j k gj gk w b e W B E 0 ^ $ G）+ gg（transitions.ts:398-416 fromG 分支，linewise 判定 motions.ts:81）。
/// 枚举成员注释标注对应按键；键→枚举解析见 <see cref="VimMotions.TryResolveSimpleKey"/>（SIMPLE_MOTIONS 14 键，
/// types.ts:135-149 —— gj/gk/G/gg 不在 SIMPLE_MOTIONS，经 g 前缀状态或独立分支进入）。
/// </summary>
public enum VimMotion
{
    /// <summary>非 motion 占位 — lineOp（dd/cc/yy）的 dot-repeat 记录用：
    /// 基准 executeLineOp 记 motion: op[0]（'d'/'c'/'y'，operators.ts:165），重放走 resolveMotion 不识别 → 无操作；
    /// C# 侧用 None 等价占位，Resolve(None) 原地不动（对齐基准 dd. 为 noop 的实际行为）。</summary>
    None,

    /// <summary>h：光标左移一个 grapheme（Cursor.ts:301-309 left）。</summary>
    CharLeft,

    /// <summary>l：光标右移一个 grapheme（Cursor.ts:311-319 right）。</summary>
    CharRight,

    /// <summary>j：下一逻辑行同列（短行钳制行尾 + grapheme 边界对齐，Cursor.ts:533-553 downLogicalLine）。</summary>
    LineDown,

    /// <summary>k：上一逻辑行同列（首行回 0，Cursor.ts:511-531 upLogicalLine）。</summary>
    LineUp,

    /// <summary>gj：下一视觉（折行）行同显示列（Cursor.ts:377-408 down — 需折行宽度）。</summary>
    VisualLineDown,

    /// <summary>gk：上一视觉行同显示列（Cursor.ts:353-375 up）。</summary>
    VisualLineUp,

    /// <summary>w：下一 vim 词首（词=字母数字下划线串或标点串，跨空白，Cursor.ts:658-689 nextVimWord）。</summary>
    WordForward,

    /// <summary>b：前一 vim 词首（Cursor.ts:737-772 prevVimWord）。</summary>
    WordBack,

    /// <summary>e：当前/下一 vim 词尾（光标落词尾字符上，Cursor.ts:691-735 endOfVimWord）。</summary>
    WordEnd,

    /// <summary>W：下一大词首（大词=非空白连续段，Cursor.ts:774-786 nextWORD）。</summary>
    BigWordForward,

    /// <summary>B：前一大词首（Cursor.ts:821-843 prevWORD）。</summary>
    BigWordBack,

    /// <summary>E：当前/下一大词尾（Cursor.ts:788-819 endOfWORD）。</summary>
    BigWordEnd,

    /// <summary>0：当前逻辑行行首（Cursor.ts:499-501 startOfLogicalLine）。</summary>
    LineStart,

    /// <summary>^：当前逻辑行首个非空白（Cursor.ts:503-509 firstNonBlankInLogicalLine）。</summary>
    FirstNonBlank,

    /// <summary>$：当前逻辑行行尾（光标落 \n 位或文末 — 基准 endOfLogicalLine 语义，Cursor.ts:495-497）。</summary>
    LineEnd,

    /// <summary>G：末行行首（count&gt;1 时第 N 行行首 — 独立分支处理，transitions.ts:146-158；
    /// operator 变体 operators.ts:524-539）。</summary>
    BufferEnd,

    /// <summary>gg：首行行首（count&gt;1 时第 N 行行首，transitions.ts:398-416；operator 变体 operators.ts:541-556）。</summary>
    FirstLine,
}

/// <summary>
/// vim motion 原语层 — 基准 src/vim/motions.ts 纯函数 + src/utils/Cursor.ts 的词移动/行定位/grapheme 边界原语。
/// 全部为无副作用纯计算（text + offset 进，新 offset 出）；光标模型 = 全文 char 偏移（0..len），
/// 与 InputBuffer.CursorCol 同构（InputBuffer.cs:38）；grapheme 边界用 GraphemeSegmenter
///（对齐基准 measuredText.nextOffset/prevOffset/snapToGraphemeBoundary）。
/// </summary>
public static class VimMotions
{
    /// <summary>
    /// 解析 SIMPLE_MOTIONS 键 — 基准 types.ts:135-149（h l j k w b e W B E 0 ^ $）。
    /// </summary>
    /// <param name="key">单字符按键。</param>
    /// <param name="motion">解析出的 motion。</param>
    /// <returns>是否属于简单 motion 键（gj/gk/G/gg 不在此列）。</returns>
    public static bool TryResolveSimpleKey(char key, out VimMotion motion)
    {
        motion = key switch
        {
            'h' => VimMotion.CharLeft,
            'l' => VimMotion.CharRight,
            'j' => VimMotion.LineDown,
            'k' => VimMotion.LineUp,
            'w' => VimMotion.WordForward,
            'b' => VimMotion.WordBack,
            'e' => VimMotion.WordEnd,
            'W' => VimMotion.BigWordForward,
            'B' => VimMotion.BigWordBack,
            'E' => VimMotion.BigWordEnd,
            '0' => VimMotion.LineStart,
            '^' => VimMotion.FirstNonBlank,
            '$' => VimMotion.LineEnd,
            _ => VimMotion.None,
        };
        return motion != VimMotion.None;
    }

    /// <summary>
    /// motion 是否 inclusive（含目标位置字符 — 算子范围右端扩一 grapheme）—
    /// 基准 motions.ts:72-74 `'eE$'.includes(key)`（仅 e/E/$ 三键）。
    /// </summary>
    public static bool IsInclusive(VimMotion motion) =>
        motion is VimMotion.WordEnd or VimMotion.BigWordEnd or VimMotion.LineEnd;

    /// <summary>
    /// motion 是否 linewise（算子按整行扩展）— 基准 motions.ts:80-82
    /// `'jkG'.includes(key) || key === 'gg'`（gj/gk 刻意不算 — 注释引 :help gj，characterwise exclusive）。
    /// </summary>
    public static bool IsLinewise(VimMotion motion) =>
        motion is VimMotion.LineDown or VimMotion.LineUp or VimMotion.BufferEnd or VimMotion.FirstLine;

    /// <summary>
    /// 应用 motion（含数量前缀）— 基准 motions.ts:13-25 resolveMotion：
    /// 循环 count 次单步，单步无位移即提前终止（j/k 撞边界后不再累计）。
    /// </summary>
    /// <param name="text">全文镜像。</param>
    /// <param name="offset">起始光标偏移。</param>
    /// <param name="motion">motion 种类。</param>
    /// <param name="count">数量前缀（&lt;1 视为 1 — 基准 count 恒 ≥1）。</param>
    /// <param name="wrapWidth">视觉行折行内容宽（gj/gk 用；对齐基准 columns 语义）。</param>
    /// <returns>目标光标偏移。</returns>
    public static int Resolve(string text, int offset, VimMotion motion, int count, int wrapWidth)
    {
        var result = offset;
        var steps = Math.Max(1, count);
        for (var i = 0; i < steps; i++)
        {
            var next = ApplySingle(text, result, motion, wrapWidth);
            if (next == result)
            {
                break;
            }

            result = next;
        }

        return result;
    }

    /// <summary>
    /// 应用单步 motion — 基准 motions.ts:30-67 applySingleMotion 的 switch 全集
    ///（未知键含 None 原地不动 — 对齐 default: return cursor）。
    /// </summary>
    public static int ApplySingle(string text, int offset, VimMotion motion, int wrapWidth) =>
        motion switch
        {
            VimMotion.CharLeft => PrevGraphemeStart(text, offset),
            VimMotion.CharRight => NextGraphemeStart(text, offset),
            VimMotion.LineDown => DownLogicalLine(text, offset),
            VimMotion.LineUp => UpLogicalLine(text, offset),
            VimMotion.VisualLineDown => MoveVisualLine(text, offset, 1, wrapWidth),
            VimMotion.VisualLineUp => MoveVisualLine(text, offset, -1, wrapWidth),
            VimMotion.WordForward => NextVimWord(text, offset),
            VimMotion.WordBack => PrevVimWord(text, offset),
            VimMotion.WordEnd => EndOfVimWord(text, offset),
            VimMotion.BigWordForward => NextBigWord(text, offset),
            VimMotion.BigWordBack => PrevBigWord(text, offset),
            VimMotion.BigWordEnd => EndOfBigWord(text, offset),
            VimMotion.LineStart => LineStart(text, offset),
            VimMotion.FirstNonBlank => FirstNonBlank(text, offset),
            VimMotion.LineEnd => LineEnd(text, offset),
            VimMotion.BufferEnd or VimMotion.FirstLine => offset, // G/gg 的 count 语义独立处理，单步不动
            _ => offset,
        };

    /// <summary>
    /// 字符查找 — 基准 Cursor.ts:1062-1105 findCharacter：
    /// 正向从光标下一 grapheme 起扫（跳过自身），反向从光标前一 grapheme 起扫；
    /// t/T 的 till 语义钳制不越过光标（max/min）；目标字符与整个 grapheme 比较（多字符 grapheme 不匹配单字符）。
    /// </summary>
    /// <param name="text">全文镜像。</param>
    /// <param name="offset">光标偏移。</param>
    /// <param name="ch">目标字符（单键字符）。</param>
    /// <param name="kind">查找种类（f/F/t/T）。</param>
    /// <param name="count">第 N 次出现（&lt;1 视为 1）。</param>
    /// <returns>目标偏移；未找到返回 null。</returns>
    public static int? FindCharacter(string text, int offset, string ch, VimFindKind kind, int count)
    {
        var forward = kind is VimFindKind.ToForward or VimFindKind.TillForward;
        var till = kind is VimFindKind.TillForward or VimFindKind.TillBackward;
        var occurrences = 0;
        var steps = Math.Max(1, count);

        if (forward)
        {
            var pos = NextGraphemeStart(text, offset);
            while (pos < text.Length)
            {
                if (GraphemeEquals(text, pos, ch))
                {
                    occurrences++;
                    if (occurrences == steps)
                    {
                        return till
                            ? Math.Max(offset, PrevGraphemeStart(text, pos))
                            : pos;
                    }
                }

                pos = NextGraphemeStart(text, pos);
            }
        }
        else
        {
            if (offset == 0)
            {
                return null;
            }

            var pos = PrevGraphemeStart(text, offset);
            while (pos >= 0)
            {
                if (GraphemeEquals(text, pos, ch))
                {
                    occurrences++;
                    if (occurrences == steps)
                    {
                        return till
                            ? Math.Min(offset, NextGraphemeStart(text, pos))
                            : pos;
                    }
                }

                if (pos == 0)
                {
                    break;
                }

                pos = PrevGraphemeStart(text, pos);
            }
        }

        return null;
    }

    // ====================================================================
    // 词移动原语 — 基准 Cursor.ts:653-843（vim word / WORD 语义）
    // ====================================================================

    /// <summary>
    /// 下一 vim 词首 — 基准 Cursor.ts:658-689 nextVimWord：
    /// 跳过当前词剩余部分（词=word 字符串或标点串，二选一），再跳过空白段；末尾钳制文本尾。
    /// 换行符属空白 — 词移动天然跨行（对齐基准）。
    /// </summary>
    public static int NextVimWord(string text, int offset)
    {
        if (offset >= text.Length)
        {
            return offset;
        }

        var pos = offset;
        var current = GraphemeAt(text, pos);
        if (current.Length == 0)
        {
            return offset;
        }

        if (IsVimWordChar(current))
        {
            while (pos < text.Length && IsVimWordChar(GraphemeAt(text, pos)))
            {
                pos = NextGraphemeStart(text, pos);
            }
        }
        else if (IsVimPunctuation(current))
        {
            while (pos < text.Length && IsVimPunctuation(GraphemeAt(text, pos)))
            {
                pos = NextGraphemeStart(text, pos);
            }
        }

        while (pos < text.Length && IsVimWhitespace(GraphemeAt(text, pos)))
        {
            pos = NextGraphemeStart(text, pos);
        }

        return pos;
    }

    /// <summary>
    /// 当前/下一 vim 词尾 — 基准 Cursor.ts:691-735 endOfVimWord：
    /// 先跳过光标处 grapheme（在词中时留在本词），跳过空白段，再把光标推进到目标词最后一个 grapheme 的起点；
    /// 文本尾返回 len。
    /// </summary>
    public static int EndOfVimWord(string text, int offset)
    {
        if (offset >= text.Length)
        {
            return offset;
        }

        var pos = NextGraphemeStart(text, offset);
        while (pos < text.Length && IsVimWhitespace(GraphemeAt(text, pos)))
        {
            pos = NextGraphemeStart(text, pos);
        }

        if (pos >= text.Length)
        {
            return text.Length;
        }

        var current = GraphemeAt(text, pos);
        if (IsVimWordChar(current))
        {
            while (pos < text.Length)
            {
                var next = NextGraphemeStart(text, pos);
                if (next >= text.Length || !IsVimWordChar(GraphemeAt(text, next)))
                {
                    break;
                }

                pos = next;
            }
        }
        else if (IsVimPunctuation(current))
        {
            while (pos < text.Length)
            {
                var next = NextGraphemeStart(text, pos);
                if (next >= text.Length || !IsVimPunctuation(GraphemeAt(text, next)))
                {
                    break;
                }

                pos = next;
            }
        }

        return pos;
    }

    /// <summary>
    /// 前一 vim 词首 — 基准 Cursor.ts:737-772 prevVimWord：
    /// 先退一 grapheme，跳过空白段，再回退到目标词首；起始空白无前词时回 0。
    /// </summary>
    public static int PrevVimWord(string text, int offset)
    {
        if (offset == 0)
        {
            return offset;
        }

        var pos = PrevGraphemeStart(text, offset);
        while (pos > 0 && IsVimWhitespace(GraphemeAt(text, pos)))
        {
            pos = PrevGraphemeStart(text, pos);
        }

        if (pos == 0 && IsVimWhitespace(GraphemeAt(text, 0)))
        {
            return 0;
        }

        var current = GraphemeAt(text, pos);
        if (IsVimWordChar(current))
        {
            while (pos > 0)
            {
                var prev = PrevGraphemeStart(text, pos);
                if (!IsVimWordChar(GraphemeAt(text, prev)))
                {
                    break;
                }

                pos = prev;
            }
        }
        else if (IsVimPunctuation(current))
        {
            while (pos > 0)
            {
                var prev = PrevGraphemeStart(text, pos);
                if (!IsVimPunctuation(GraphemeAt(text, prev)))
                {
                    break;
                }

                pos = prev;
            }
        }

        return pos;
    }

    /// <summary>
    /// 下一大词首 — 基准 Cursor.ts:774-786 nextWORD：
    /// 跳过当前非空白段，再跳过空白段（grapheme 步进；大词=非空白连续段，词内标点不分隔）。
    /// </summary>
    public static int NextBigWord(string text, int offset)
    {
        var pos = offset;
        while (!IsOverWhitespace(text, pos) && pos < text.Length)
        {
            pos = NextGraphemeStart(text, pos);
        }

        while (IsOverWhitespace(text, pos) && pos < text.Length)
        {
            pos = NextGraphemeStart(text, pos);
        }

        return pos;
    }

    /// <summary>
    /// 当前/下一大词尾 — 基准 Cursor.ts:788-819 endOfWORD（递归迭代化）：
    /// 已在大词尾时跳到下一大词的尾；落在空白上时先 nextWORD；否则推进到本大词尾。
    /// </summary>
    public static int EndOfBigWord(string text, int offset)
    {
        if (offset >= text.Length)
        {
            return offset;
        }

        var pos = offset;
        while (true)
        {
            if (pos >= text.Length)
            {
                return pos;
            }

            var right = NextGraphemeStart(text, pos);
            var atEndOfWord = !IsOverWhitespace(text, pos)
                && (IsOverWhitespace(text, right) || right >= text.Length);
            if (atEndOfWord)
            {
                pos = right; // 递归 right().endOfWORD() 的迭代化（基准 802-806）
                continue;
            }

            if (IsOverWhitespace(text, pos))
            {
                pos = NextBigWord(text, pos);
            }

            while (!IsOverWhitespace(text, NextGraphemeStart(text, pos)) && pos < text.Length)
            {
                pos = NextGraphemeStart(text, pos);
            }

            return pos;
        }
    }

    /// <summary>
    /// 前一大词首 — 基准 Cursor.ts:821-843 prevWORD：
    /// 已在大词首（左侧是空白）时先退出去，跳过空白段，再回退到目标大词首。
    /// </summary>
    public static int PrevBigWord(string text, int offset)
    {
        var pos = offset;
        var left = PrevGraphemeStart(text, pos);
        if (IsOverWhitespace(text, left))
        {
            pos = left;
        }

        while (IsOverWhitespace(text, pos) && pos > 0)
        {
            pos = PrevGraphemeStart(text, pos);
        }

        if (!IsOverWhitespace(text, pos))
        {
            while (!IsOverWhitespace(text, PrevGraphemeStart(text, pos)) && pos > 0)
            {
                pos = PrevGraphemeStart(text, pos);
            }
        }

        return pos;
    }

    // ====================================================================
    // 行定位原语 — 基准 Cursor.ts:462-553（逻辑行）+ 1004-1036（G/gg/goToLine）
    // ====================================================================

    /// <summary>光标所在逻辑行行首 — 基准 findLogicalLineStart（Cursor.ts:463-466）。</summary>
    public static int LineStart(string text, int offset)
    {
        var clamped = Math.Clamp(offset, 0, text.Length);
        for (var i = clamped - 1; i >= 0; i--)
        {
            if (text[i] == '\n')
            {
                return i + 1;
            }
        }

        return 0;
    }

    /// <summary>光标所在逻辑行行尾 — 基准 findLogicalLineEnd（Cursor.ts:468-471）：
    /// 下一 \n 位置或文本尾（注意：基准 $ 落在 \n 位/文末，不回退到最后字符 — 对齐基准语义）。</summary>
    public static int LineEnd(string text, int offset)
    {
        var clamped = Math.Clamp(offset, 0, text.Length);
        for (var i = clamped; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                return i;
            }
        }

        return text.Length;
    }

    /// <summary>当前逻辑行首个非空白 — 基准 firstNonBlankInLogicalLine（Cursor.ts:503-509）：
    /// 无非空白字符时回行首（基准 match?.index ?? 0）。</summary>
    public static int FirstNonBlank(string text, int offset)
    {
        var start = LineStart(text, offset);
        for (var i = start; i < text.Length && text[i] != '\n'; i++)
        {
            if (!char.IsWhiteSpace(text[i]))
            {
                return i;
            }
        }

        return start;
    }

    /// <summary>上一逻辑行同列 — 基准 upLogicalLine（Cursor.ts:511-531）：
    /// 首行回 0；短行钳制行尾；落点 grapheme 边界对齐（createCursorWithColumn，Cursor.ts:483-493）。</summary>
    public static int UpLogicalLine(string text, int offset)
    {
        var currentStart = LineStart(text, offset);
        if (currentStart == 0)
        {
            return 0;
        }

        var column = offset - currentStart;
        var prevLineEnd = currentStart - 1; // 换行符位置
        var prevLineStart = LineStart(text, prevLineEnd);
        return ColumnClampedOffset(text, prevLineStart, prevLineEnd, column);
    }

    /// <summary>下一逻辑行同列 — 基准 downLogicalLine（Cursor.ts:533-553）：末行回文本尾。</summary>
    public static int DownLogicalLine(string text, int offset)
    {
        var currentEnd = LineEnd(text, offset);
        if (currentEnd >= text.Length)
        {
            return text.Length;
        }

        var column = offset - LineStart(text, offset);
        var nextLineStart = currentEnd + 1;
        var nextLineEnd = LineEnd(text, nextLineStart);
        return ColumnClampedOffset(text, nextLineStart, nextLineEnd, column);
    }

    /// <summary>末行行首 — 基准 startOfLastLine（Cursor.ts:1009-1020）：最后 \n 后一位置；无 \n 回 0。</summary>
    public static int LastLineStart(string text)
    {
        for (var i = text.Length - 1; i >= 0; i--)
        {
            if (text[i] == '\n')
            {
                return i + 1;
            }
        }

        return 0;
    }

    /// <summary>第 N 行行首（1 起，钳制行界）— 基准 goToLine（Cursor.ts:1022-1032）。</summary>
    public static int GoToLine(string text, int lineNumber)
    {
        var lines = SplitLines(text);
        var targetLine = Math.Clamp(lineNumber - 1, 0, lines.Count - 1);
        var offset = 0;
        for (var i = 0; i < targetLine; i++)
        {
            offset += lines[i].Length + 1;
        }

        return offset;
    }

    /// <summary>
    /// 逻辑行总数（\n 拆分；空文本 1 行）— 基准 text.split('\n') 语义
    ///（PromptInputBox.SplitLogicalLines 同构，此处引擎自带镜像，零依赖 InputBuffer）。
    /// </summary>
    public static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                lines.Add(text[start..i]);
                start = i + 1;
            }
        }

        lines.Add(text[start..]);
        return lines;
    }

    /// <summary>
    /// 偏移所在逻辑行索引（偏移前换行计数）— 基准 operators.ts:111
    /// `countCharInString(text.slice(0, offset), '\n')`（executeLineOp 的逻辑行定位）。
    /// </summary>
    public static int LineIndexOf(string text, int offset)
    {
        var clamped = Math.Clamp(offset, 0, text.Length);
        var line = 0;
        for (var i = 0; i < clamped; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    /// <summary>
    /// 逻辑行行首全局偏移（lines 快照版 — 基准 operators.ts:425-427 getLineStartOffset）。
    /// </summary>
    public static int LineStartOffset(List<string> lines, int lineIndex)
    {
        var offset = 0;
        for (var i = 0; i < lineIndex && i < lines.Count; i++)
        {
            offset += lines[i].Length + 1;
        }

        return offset;
    }

    // ====================================================================
    // 视觉行移动（gj/gk）— 基准 Cursor.ts:353-408 up/down（折行投影）
    // ====================================================================

    /// <summary>
    /// 视觉行垂直移动 — 基准 up()/down()（Cursor.ts:353-408）：
    /// 同显示列移到相邻视觉行（grapheme 边界对齐，短行钳制行尾）；首/末视觉行外原地不动。
    /// 折行投影复用 InputLineLayout.Wrap + PromptInputBox.LocateVisualCursor（与 InputBuffer 视觉行同一套坐标系）。
    /// </summary>
    /// <param name="text">全文镜像。</param>
    /// <param name="offset">光标偏移。</param>
    /// <param name="dy">视觉行增量（gj=+1 / gk=-1）。</param>
    /// <param name="wrapWidth">折行内容宽（对齐基准 columns 折行语义；&lt;1 归一为 1）。</param>
    public static int MoveVisualLine(string text, int offset, int dy, int wrapWidth)
    {
        var visualLines = InputLineLayout.Wrap(text, Math.Max(1, wrapWidth));
        var (index, displayCol) = PromptInputBox.LocateVisualCursor(text, visualLines, offset);
        var target = index + dy;
        if (target < 0 || target >= visualLines.Count)
        {
            return offset;
        }

        var logicalLines = SplitLines(text);
        var targetVisual = visualLines[target];
        var lineText = logicalLines[Math.Clamp(targetVisual.LogicalLine, 0, logicalLines.Count - 1)];
        var lineStart = LineStartOffset(logicalLines, targetVisual.LogicalLine) + targetVisual.StartOffset;
        return lineStart + OffsetForDisplayCol(lineText, targetVisual, displayCol);
    }

    /// <summary>视觉行内显示列 → char 偏移（逐 grapheme 累计宽度，超行尾钳制 — InputBuffer 同构逻辑）。</summary>
    private static int OffsetForDisplayCol(string lineText, VisualLine visual, int displayCol)
    {
        var pos = visual.StartOffset;
        var width = 0;
        while (pos < visual.StartOffset + visual.Length && width < displayCol)
        {
            if (!GraphemeSegmenter.TryNext(lineText.AsSpan(), pos, out var next))
            {
                break;
            }

            width += RuneWidth.WidthOfGrapheme(lineText.AsSpan(pos, next - pos));
            pos = next;
        }

        return pos - visual.StartOffset;
    }

    // ====================================================================
    // grapheme 边界原语 — 基准 measuredText.nextOffset/prevOffset/snapToGraphemeBoundary
    // ====================================================================

    /// <summary>光标右邻 grapheme 终点（末尾钳制 len — 对齐 InputBuffer.NextGraphemeStart）。</summary>
    public static int NextGraphemeStart(string text, int offset)
    {
        var start = Math.Clamp(offset, 0, text.Length);
        return start < text.Length && GraphemeSegmenter.TryNext(text.AsSpan(), start, out var end)
            ? end
            : text.Length;
    }

    /// <summary>光标左邻 grapheme 起点（0 钳制 — 对齐 InputBuffer.PreviousGraphemeStart）。</summary>
    public static int PrevGraphemeStart(string text, int offset)
    {
        var cursor = Math.Clamp(offset, 0, text.Length);
        var prevStart = 0;
        var start = 0;
        while (start < cursor && GraphemeSegmenter.TryNext(text.AsSpan(), start, out var end))
        {
            if (end >= cursor)
            {
                return start;
            }

            prevStart = start;
            start = end;
        }

        return prevStart;
    }

    /// <summary>
    /// 偏移吸附到不大于自身的最近 grapheme 边界 — 基准 snapToGraphemeBoundary
    ///（Cursor.ts:1516-1529：最大边界 ≤ offset；用于 j/k 列钳制后避免落在 grapheme 中间）。
    /// </summary>
    public static int SnapToGraphemeBoundary(string text, int offset)
    {
        if (offset <= 0)
        {
            return 0;
        }

        if (offset >= text.Length)
        {
            return text.Length;
        }

        var boundary = 0;
        var start = 0;
        while (start <= offset && GraphemeSegmenter.TryNext(text.AsSpan(), start, out var end))
        {
            if (end > offset)
            {
                break;
            }

            boundary = end;
            start = end;
        }

        return boundary;
    }

    /// <summary>取 [offset, 下一 grapheme 边界) 的 grapheme 文本（越界返回空串 — 基准 graphemeAt 语义）。</summary>
    public static string GraphemeAt(string text, int offset)
    {
        if (offset < 0 || offset >= text.Length)
        {
            return string.Empty;
        }

        return GraphemeSegmenter.TryNext(text.AsSpan(), offset, out var end)
            ? text[offset..end]
            : string.Empty;
    }

    /// <summary>末尾 grapheme 长度（空文本返回 1 — 对齐基准 lastGrapheme(...).length || 1，operators.ts:146-148）。</summary>
    public static int LastGraphemeLength(string text)
    {
        if (text.Length == 0)
        {
            return 1;
        }

        var start = 0;
        var lastStart = 0;
        while (start < text.Length && GraphemeSegmenter.TryNext(text.AsSpan(), start, out var end))
        {
            lastStart = start;
            start = end;
        }

        return text.Length - lastStart;
    }

    /// <summary>grapheme 与查找字符相等（长度一致且逐字符相同 — 基准 grapheme === char，多字符 grapheme 不匹配）。</summary>
    private static bool GraphemeEquals(string text, int offset, string ch)
    {
        if (string.IsNullOrEmpty(ch))
        {
            return false;
        }

        if (!GraphemeSegmenter.TryNext(text.AsSpan(), offset, out var end))
        {
            return false;
        }

        if (end - offset != ch.Length)
        {
            return false;
        }

        for (var i = 0; i < ch.Length; i++)
        {
            if (text[offset + i] != ch[i])
            {
                return false;
            }
        }

        return true;
    }

    // ====================================================================
    // 词字符分类 — 基准 Cursor.ts:133-143（VIM_WORD_CHAR_REGEX / WHITESPACE_REGEX）
    // ====================================================================

    /// <summary>
    /// vim 词字符 — 基准 `/^[\p{L}\p{N}\p{M}_]$/u`（Cursor.ts:134）：
    /// 字母/数字/记号/下划线的单字符 grapheme；多字符 grapheme（emoji 链等）不属词字符
    ///（基准正则锚定单字符 — 多 code unit 必不匹配，按标点处理）。
    /// </summary>
    public static bool IsVimWordChar(ReadOnlySpan<char> grapheme)
    {
        return grapheme.Length == 1
            && (char.IsLetter(grapheme[0]) || char.IsNumber(grapheme[0]) || IsMark(grapheme[0]) || grapheme[0] == '_');
    }

    /// <summary>
    /// vim 空白 — 基准 `/\s/` 无锚定 test（Cursor.ts:135,140-141）：grapheme 内任一字符为空白即真
    ///（组合附加符不空白，实际等价首字符；char.IsWhiteSpace 与 JS \s 在 \ufeff/\x85 上有边缘差异，取近似）。
    /// </summary>
    public static bool IsVimWhitespace(ReadOnlySpan<char> grapheme)
    {
        foreach (var c in grapheme)
        {
            if (char.IsWhiteSpace(c))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>vim 标点 — 基准 isVimPunctuation（Cursor.ts:142-143）：非空、非空白、非词字符。</summary>
    public static bool IsVimPunctuation(ReadOnlySpan<char> grapheme) =>
        grapheme.Length > 0 && !IsVimWhitespace(grapheme) && !IsVimWordChar(grapheme);

    /// <summary>光标处单字符是否空白 — 基准 isOverWhitespace（Cursor.ts:986-989：text[offset] 单字符 test；越界 false）。</summary>
    private static bool IsOverWhitespace(string text, int offset) =>
        offset >= 0 && offset < text.Length && char.IsWhiteSpace(text[offset]);

    /// <summary>Unicode Mark 类别（\p{M} = Mn/Mc/Me — 基准词字符正则的记号段）。</summary>
    private static bool IsMark(char c) =>
        char.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark;

    /// <summary>
    /// 列钳制定位 — 基准 createCursorWithColumn（Cursor.ts:483-493）：
    /// 列钳制到行内长度后偏移吸附 grapheme 边界（向下吸附 — snapToGraphemeBoundary ≤offset 最大边界）。
    /// </summary>
    private static int ColumnClampedOffset(string text, int lineStart, int lineEnd, int column)
    {
        var lineLength = lineEnd - lineStart;
        var clamped = Math.Min(column, lineLength);
        return SnapToGraphemeBoundary(text, lineStart + clamped);
    }
}
