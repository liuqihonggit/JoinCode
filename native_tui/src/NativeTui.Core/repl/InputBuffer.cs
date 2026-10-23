namespace NativeTui.Core.Repl;

/// <summary>
/// 输入缓冲编辑器 — 对齐基准 hooks/useTextInput 的编辑语义 + PromptInput.tsx 的多行/粘贴/undo 增强。
/// 光标为文本内偏移（0..Text.Length），所有操作边界钳制；Render 时由 PromptInputBox 消费。
/// 多行：InsertNewline 插入换行符拆逻辑行，Home/End/Kill 收敛为行内语义（单行时与全文语义逐字节一致），
/// MoveCursorVertically 跨行垂直移动保持目标列（短行钳制行尾）。
/// 粘贴：Paste 入口经 PastedContentTracker 清洗+折叠登记（显示 [Pasted text #N]，提交取 ExpandedText 全文）。
/// undo：结构性编辑前压快照栈（容量 100，相同文本去重），Undo 弹栈恢复文本与光标（基准 ctrl+_/ctrl+shift+-）。
/// </summary>
public sealed class InputBuffer
{
    /// <summary>undo 快照栈容量（超容量淘汰最旧）。</summary>
    private const int UndoCapacity = 100;

    private readonly StringBuilder _text = new();
    private readonly PastedContentTracker _pasteTracker = new();
    private readonly List<UndoSnapshot> _undoStack = [];

    /// <summary>创建空缓冲。</summary>
    public InputBuffer()
    {
    }

    private InputBuffer(string text, int cursorCol)
    {
        _text.Append(text);
        CursorCol = cursorCol;
    }

    /// <summary>工厂：指定文本与光标位置（测试/恢复用）。</summary>
    public static InputBuffer Of(string text, int cursorCol) => new(text, cursorCol);

    /// <summary>当前文本。</summary>
    public string Text => _text.ToString();

    /// <summary>光标文本内偏移（0..Text.Length）。</summary>
    public int CursorCol { get; private set; }

    /// <summary>是否含换行符（多行形态 — 单行路径全部走原语义零变化）。</summary>
    public bool IsMultiline
    {
        get
        {
            for (var i = 0; i < _text.Length; i++)
            {
                if (_text[i] == '\n')
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>逻辑行数（按 \n 拆分；空文本为 1 行）。</summary>
    public int LineCount
    {
        get
        {
            var count = 1;
            for (var i = 0; i < _text.Length; i++)
            {
                if (_text[i] == '\n')
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>光标所在逻辑行索引（0 起）。</summary>
    public int CursorLineIndex => LineOf(CursorCol);

    /// <summary>光标在所在逻辑行内的偏移（0 起，不含换行符）。</summary>
    public int CursorLineColumn => ColumnOf(CursorCol);

    /// <summary>undo 可用（快照栈非空）。</summary>
    public bool CanUndo => _undoStack.Count > 0;

    /// <summary>提交全文（折叠引用展开回登记原文 — 显示折叠形态，提交全文语义）。</summary>
    public string ExpandedText => _pasteTracker.ExpandReferences(Text);

    /// <summary>在光标处插入字符，光标后移。</summary>
    public void Insert(char ch)
    {
        PushUndoSnapshot();
        BeginEditText();
        _text.Insert(CursorCol, ch);
        CursorCol++;
    }

    /// <summary>在光标处插入换行符（基准 shift+enter 换行）— 光标落到新行行首。</summary>
    public void InsertNewline()
    {
        PushUndoSnapshot();
        BeginEditText();
        _text.Insert(CursorCol, '\n');
        CursorCol++;
    }

    /// <summary>删除光标前字符（光标 0 时无操作）— 行首时删除前一行的换行符（合并两行）；
    /// grapheme 语义（DSG044 L2）：删光标前完整 cluster（emoji 代理对/ZWJ 链/组合符附加一次删整块，
    /// 不留半代理对 — 对齐基准 deleteTokenBefore 的 grapheme 边界）。</summary>
    public void Backspace()
    {
        BeginEditText();
        if (CursorCol == 0)
        {
            return;
        }

        PushUndoSnapshot();
        var start = PreviousGraphemeStart(CursorCol);
        _text.Remove(start, CursorCol - start);
        CursorCol = start;
    }

    /// <summary>删除光标处字符（光标在末尾时无操作）。</summary>
    public void Delete()
    {
        BeginEditText();
        if (CursorCol >= _text.Length)
        {
            return;
        }

        PushUndoSnapshot();
        _text.Remove(CursorCol, 1);
    }

    /// <summary>光标左移一个 grapheme（钳制 0；跨换行符移动 — \n 占一个偏移；
    /// grapheme 语义 DSG044 L2：emoji 代理对/ZWJ 链/组合符附加一次跨整块 —
    /// 对齐基准 Cursor.ts prevOffset 跳 grapheme 边界）。</summary>
    public void Left()
    {
        BeginEditText();
        CursorCol = PreviousGraphemeStart(CursorCol);
    }

    /// <summary>光标右移一个 grapheme（钳制文本长度；跨换行符移动；
    /// grapheme 语义 DSG044 L2 — 对齐基准 Cursor.ts nextOffset）。</summary>
    public void Right()
    {
        BeginEditText();
        CursorCol = NextGraphemeStart(CursorCol);
    }

    /// <summary>光标左侧最近的 grapheme 起点（按键级调用 — ToString 分配可接受；
    /// 光标在 cluster 中间[异常态]归到本 cluster 起点）。</summary>
    private int PreviousGraphemeStart(int cursor)
    {
        var text = _text.ToString();
        var prevStart = 0;
        var start = 0;
        while (start < cursor && GraphemeSegmenter.TryNext(text, start, out var end))
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

    /// <summary>光标右侧最近的 grapheme 终点（光标在 cluster 中间[异常态]归到本 cluster 终点）。</summary>
    private int NextGraphemeStart(int cursor)
    {
        var text = _text.ToString();
        var start = 0;
        while (GraphemeSegmenter.TryNext(text, start, out var end))
        {
            if (start >= cursor || end > cursor)
            {
                return end;
            }

            start = end;
        }

        return _text.Length;
    }

    /// <summary>光标跳到当前逻辑行行首（单行 = 全文首，对齐既有语义）。</summary>
    public void Home()
    {
        BeginEditText();
        CursorCol -= CursorLineColumn;
    }

    /// <summary>光标跳到当前逻辑行行尾（单行 = 全文尾，对齐既有语义）。</summary>
    public void End()
    {
        BeginEditText();
        var line = CursorLineIndex;
        CursorCol = LineStartOffset(line) + LineLength(line);
    }

    /// <summary>垂直移动光标 dy 行（基准 multiline 上下键）— 保持目标列，短行钳制行尾；首/末行外无操作。</summary>
    public void MoveCursorVertically(int dy)
    {
        BeginEditText();
        var line = CursorLineIndex;
        var targetLine = Math.Clamp(line + dy, 0, LineCount - 1);
        if (targetLine == line)
        {
            return;
        }

        var column = CursorLineColumn;
        CursorCol = LineStartOffset(targetLine) + Math.Min(column, LineLength(targetLine));
    }

    /// <summary>视觉行垂直移动（DSG044 L4 — 基准 useTextInput.ts:269-316 三级回退第一级）：
    /// 软折行内上下键细移 — 同显示列移到相邻视觉行（grapheme 边界对齐，短行钳制行尾）；
    /// 首/末视觉行外返回 false（调用方走第二级逻辑行/第三级历史导航回退）。
    /// cols = 终端总宽（折行内容宽 = cols - PromptWidth，与渲染投影同公式）。</summary>
    public bool TryMoveVisualVertically(int dy, int cols)
    {
        BeginEditText();
        var (visualLines, logicalLines, index, displayCol) = VisualContext(cols);
        var target = index + dy;
        if (target < 0 || target >= visualLines.Count)
        {
            return false;
        }

        CursorCol = LogicalStart(logicalLines, visualLines[target].LogicalLine)
            + visualLines[target].StartOffset
            + OffsetForDisplayCol(logicalLines[visualLines[target].LogicalLine], visualLines[target], displayCol);
        return true;
    }

    /// <summary>视觉行 Home（DSG044 L4 — 基准 Cursor.ts:426-442）：中部 → 视觉行首；
    /// 已在视觉行首 → 上一视觉行尾（连按上跳）；视觉行 0 首 → 保持 0（全局首）。</summary>
    public void HomeVisual(int cols)
    {
        BeginEditText();
        var (visualLines, logicalLines, index, displayCol) = VisualContext(cols);
        if (displayCol > 0)
        {
            CursorCol = LogicalStart(logicalLines, visualLines[index].LogicalLine) + visualLines[index].StartOffset;
            return;
        }

        if (index == 0)
        {
            return;
        }

        var previous = visualLines[index - 1];
        CursorCol = LogicalStart(logicalLines, previous.LogicalLine) + previous.StartOffset + previous.Length;
    }

    /// <summary>视觉行 End（DSG044 L4 — 基准 Cursor.ts:455-460 视觉行尾语义）。</summary>
    public void EndVisual(int cols)
    {
        BeginEditText();
        var (visualLines, logicalLines, index, _) = VisualContext(cols);
        var current = visualLines[index];
        CursorCol = LogicalStart(logicalLines, current.LogicalLine) + current.StartOffset + current.Length;
    }

    /// <summary>视觉行上下文（折行投影 + 光标视觉行定位）— cols 语义同 <see cref="TryMoveVisualVertically"/>。</summary>
    private (List<VisualLine> VisualLines, List<string> LogicalLines, int Index, int DisplayCol) VisualContext(int cols)
    {
        var text = _text.ToString();
        var visualLines = InputLineLayout.Wrap(text, Math.Max(1, cols - PromptInputBox.PromptWidth));
        var logicalLines = PromptInputBox.SplitLogicalLines(text);
        var (index, displayCol) = PromptInputBox.LocateVisualCursor(text, visualLines, CursorCol);
        return (visualLines, logicalLines, index, displayCol);
    }

    /// <summary>逻辑行全局 char 起点（逻辑行长度 + 换行符累计）。</summary>
    private static int LogicalStart(List<string> logicalLines, int logicalLine)
    {
        var start = 0;
        for (var i = 0; i < logicalLine; i++)
        {
            start += logicalLines[i].Length + 1;
        }

        return start;
    }

    /// <summary>视觉行内显示列 → char 偏移（逐 grapheme 累计宽度，超行尾钳制）。</summary>
    private static int OffsetForDisplayCol(string lineText, VisualLine visual, int displayCol)
    {
        var pos = visual.StartOffset;
        var width = 0;
        while (pos < visual.StartOffset + visual.Length && width < displayCol)
        {
            if (!GraphemeSegmenter.TryNext(lineText, pos, out var next))
            {
                break;
            }

            width += RuneWidth.WidthOfGrapheme(lineText.AsSpan(pos, next - pos));
            pos = next;
        }

        return pos - visual.StartOffset;
    }

    /// <summary>清空文本并重置光标（提交语义 — 同步清空 undo 栈与粘贴登记簿，对齐基准提交后 clearBuffer）。</summary>
    public void Clear()
    {
        BeginEditText();
        _text.Clear();
        CursorCol = 0;
        _undoStack.Clear();
        _pasteTracker.Clear();
    }

    /// <summary>全量替换文本（历史导航/补全接受用）— cursorToStart=true 光标置行首（基准历史导航语义），否则置末尾。</summary>
    public void Replace(string text, bool cursorToStart = false)
    {
        PushUndoSnapshot();
        BeginEditText();
        _text.Clear();
        _text.Append(text);
        CursorCol = cursorToStart ? 0 : text.Length;
    }

    /// <summary>全量替换文本并指定光标位置（S10 接线 — @ 文件补全替换写回：token 段替换后光标置
    /// 替换段末尾而非全文末尾，中部替换不跳尾）；cursorCol 钳制到 [0, text.Length]。</summary>
    public void ReplaceAt(string text, int cursorCol)
    {
        PushUndoSnapshot();
        BeginEditText();
        _text.Clear();
        _text.Append(text);
        CursorCol = Math.Clamp(cursorCol, 0, text.Length);
    }

    /// <summary>
    /// 粘贴入口（bracketed paste 事件到达）— 清洗（去 ANSI/\r 归一/Tab 展开）后判定折叠：
    /// 超阈值登记为 [Pasted text #N +M lines] 引用插入，否则原文直插；显示折叠形态，提交取 ExpandedText。
    /// </summary>
    public void Paste(string rawText, int maxVisibleLines = PastedContentTracker.DefaultMaxVisibleLines)
    {
        InsertText(_pasteTracker.RegisterPaste(rawText, maxVisibleLines));
    }

    /// <summary>词左移（Alt+B / Ctrl+Left）— 跳到前一词首：越过非词字符到词首；CJK 单字成词。</summary>
    public void WordLeft()
    {
        BeginEditText();
        CursorCol = PrevWordStart(CursorCol);
    }

    /// <summary>词右移（Alt+F / Ctrl+Right）— 跳到下一个词首：词内跳过本词剩余部分再越过非词字符；CJK 单字成词。</summary>
    public void WordRight()
    {
        BeginEditText();
        CursorCol = NextWordStart(CursorCol);
    }

    /// <summary>剪切到当前逻辑行行首（Ctrl+U）— 删除光标前行内文本，prepend 入 kill ring（单行 = 全文行首）。</summary>
    public void KillToLineStart()
    {
        var lineStart = LineStartOffset(CursorLineIndex);
        var killed = _text.ToString(lineStart, CursorCol - lineStart);
        if (killed.Length == 0)
        {
            return;
        }

        PushUndoSnapshot();
        _text.Remove(lineStart, CursorCol - lineStart);
        CursorCol = lineStart;
        KillRing.Push(killed, prepend: true);
    }

    /// <summary>剪切到当前逻辑行行尾（Ctrl+K）— 删除光标后行内文本（不含换行符），append 入 kill ring（单行 = 全文行尾）。</summary>
    public void KillToLineEnd()
    {
        var line = CursorLineIndex;
        var lineEnd = LineStartOffset(line) + LineLength(line);
        var killed = _text.ToString(CursorCol, lineEnd - CursorCol);
        if (killed.Length == 0)
        {
            return;
        }

        PushUndoSnapshot();
        _text.Remove(CursorCol, killed.Length);
        KillRing.Push(killed, prepend: false);
    }

    /// <summary>剪切光标前一个词（Ctrl+W / Alt+Backspace）— 词内剪到词首，prepend 入 kill ring。</summary>
    public void KillWordBefore()
    {
        var target = PrevWordStart(CursorCol);
        var killed = _text.ToString(target, CursorCol - target);
        if (killed.Length == 0)
        {
            return;
        }

        PushUndoSnapshot();
        _text.Remove(target, CursorCol - target);
        CursorCol = target;
        KillRing.Push(killed, prepend: true);
    }

    /// <summary>删除光标后一个词（Alt+D）— 不进 kill ring（对齐基准 deleteWordBefore 直删语义）。</summary>
    public void KillWordAfter()
    {
        BeginEditText();
        var target = NextWordStart(CursorCol);
        if (target == CursorCol)
        {
            return;
        }

        PushUndoSnapshot();
        _text.Remove(CursorCol, target - CursorCol);
    }

    /// <summary>yank（Ctrl+Y）— 在光标处插入 kill ring 头条；空环无操作。</summary>
    public void Yank()
    {
        KillRing.ResetKillAccumulation();
        var text = KillRing.Last;
        if (text.Length == 0)
        {
            return;
        }

        PushUndoSnapshot();
        var start = CursorCol;
        _text.Insert(CursorCol, text);
        CursorCol += text.Length;
        KillRing.RecordYank(start, text.Length);
    }

    /// <summary>yank-pop（Alt+Y）— 上次 yank 区域替换为环中下一条；无链可弹返回 false。</summary>
    public bool YankPop()
    {
        if (!KillRing.TryYankPop(out var text, out var start, out var length))
        {
            return false;
        }

        PushUndoSnapshot();
        _text.Remove(start, length);
        _text.Insert(start, text);
        CursorCol = start + text.Length;
        KillRing.UpdateYankLength(text.Length);
        return true;
    }

    /// <summary>undo（ctrl+_/ctrl+shift+-）— 弹出最近快照恢复文本与光标；栈空返回 false。</summary>
    public bool Undo()
    {
        if (_undoStack.Count == 0)
        {
            return false;
        }

        var snapshot = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        BeginEditText();
        _text.Clear();
        _text.Append(snapshot.Text);
        CursorCol = Math.Min(snapshot.CursorCol, snapshot.Text.Length);
        return true;
    }

    /// <summary>清空 undo 栈（不动画布 — 提交后由 Clear 联动，外部补收口用）。</summary>
    public void ClearUndo() => _undoStack.Clear();

    /// <summary>打断 kill 累积链（对齐基准非 kill 键 resetKillAccumulation）。</summary>
    public void ResetKillAccumulation() => KillRing.ResetKillAccumulation();

    /// <summary>打断 yank 链（对齐基准非 yank 键 resetYankState）。</summary>
    public void ResetYankState() => KillRing.ResetYankState();

    /// <summary>光标所在行索引（0 起；偏移钳制到 [0, 文本长度]）。</summary>
    public int LineOf(int offset)
    {
        var clamped = Math.Clamp(offset, 0, _text.Length);
        var line = 0;
        for (var i = 0; i < clamped; i++)
        {
            if (_text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    /// <summary>偏移在所在行内的列（0 起；偏移钳制到 [0, 文本长度]）。</summary>
    public int ColumnOf(int offset)
    {
        var clamped = Math.Clamp(offset, 0, _text.Length);
        var column = 0;
        for (var i = 0; i < clamped; i++)
        {
            if (_text[i] == '\n')
            {
                column = 0;
            }
            else
            {
                column++;
            }
        }

        return column;
    }

    /// <summary>指定逻辑行行首的文本偏移（行索引钳制到 [0, LineCount-1]）。</summary>
    public int LineStartOffset(int lineIndex)
    {
        var clamped = Math.Clamp(lineIndex, 0, LineCount - 1);
        var offset = 0;
        var line = 0;
        while (line < clamped)
        {
            if (_text[offset] == '\n')
            {
                line++;
            }

            offset++;
        }

        return offset;
    }

    /// <summary>指定逻辑行的文本（不含换行符；行索引钳制）。</summary>
    public string LineText(int lineIndex)
    {
        var start = LineStartOffset(lineIndex);
        var length = LineLength(Math.Clamp(lineIndex, 0, LineCount - 1));
        return _text.ToString(start, length);
    }

    /// <summary>指定逻辑行长度（不含换行符；行索引钳制）。</summary>
    internal int LineLength(int lineIndex)
    {
        var start = LineStartOffset(lineIndex);
        var length = 0;
        for (var i = start; i < _text.Length && _text[i] != '\n'; i++)
        {
            length++;
        }

        return length;
    }

    /// <summary>在光标处插入文本（粘贴/折叠引用统一入口）— 空文本无操作。</summary>
    private void InsertText(string text)
    {
        BeginEditText();
        if (text.Length == 0)
        {
            return;
        }

        PushUndoSnapshot();
        _text.Insert(CursorCol, text);
        CursorCol += text.Length;
    }

    /// <summary>结构性编辑前压快照（基准 pushToBuffer）— 相同文本去重，超容量淘汰最旧。</summary>
    private void PushUndoSnapshot()
    {
        var text = Text;
        if (_undoStack.Count > 0 && _undoStack[^1].Text == text)
        {
            return;
        }

        _undoStack.Add(new UndoSnapshot(text, CursorCol));
        if (_undoStack.Count > UndoCapacity)
        {
            _undoStack.RemoveAt(0);
        }
    }

    /// <summary>普通编辑前奏 — 打断 kill/yank 两条链（kill/yank 方法自身不走这里）。</summary>
    private void BeginEditText()
    {
        KillRing.ResetKillAccumulation();
        KillRing.ResetYankState();
    }

    /// <summary>词字符判定 — 字母/数字（对齐 Intl.Segmenter word-like 近似）。</summary>
    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c);

    /// <summary>CJK 宽词字符 — 单字自成一词（对齐 Intl.Segmenter 对 CJK 的逐字切分）。</summary>
    private static bool IsWideWordChar(char c) =>
        char.IsLetterOrDigit(c) && RuneWidth.Of(new Rune(c)) == 2;

    /// <summary>prevWord 起点 — 光标前最近的词首（跳过非词；词内回到词首；CJK 前一宽字即词首）。</summary>
    private int PrevWordStart(int from)
    {
        var pos = from;
        while (pos > 0 && !IsWordChar(_text[pos - 1]))
        {
            pos--;
        }

        if (pos > 0 && IsWideWordChar(_text[pos - 1]))
        {
            return pos - 1;
        }

        while (pos > 0 && IsWordChar(_text[pos - 1]))
        {
            pos--;
        }

        return pos;
    }

    /// <summary>nextWord 起点 — 光标后最近的词首（越过本词剩余部分与非词字符；CJK 宽字单字成词，下一宽字即词首）。</summary>
    private int NextWordStart(int from)
    {
        var pos = from;
        if (pos < _text.Length && IsWordChar(_text[pos]))
        {
            if (IsWideWordChar(_text[pos]))
            {
                return pos + 1;
            }

            while (pos < _text.Length && IsWordChar(_text[pos]))
            {
                pos++;
            }
        }

        while (pos < _text.Length && !IsWordChar(_text[pos]))
        {
            pos++;
        }

        return pos;
    }

    /// <summary>undo 快照 — 编辑前文本与光标（record 值相等比较用于去重）。</summary>
    private readonly record struct UndoSnapshot(string Text, int CursorCol);
}
