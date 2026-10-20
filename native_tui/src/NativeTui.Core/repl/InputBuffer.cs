namespace NativeTui.Core.Repl;

/// <summary>
/// 输入缓冲编辑器 — 对齐基准 hooks/useTextInput 的单行编辑语义。
/// 光标为文本内偏移（0..Text.Length），所有操作边界钳制；Render 时由 PromptInputBox 消费。
/// </summary>
public sealed class InputBuffer
{
    private readonly StringBuilder _text = new();

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

    /// <summary>在光标处插入字符，光标后移。</summary>
    public void Insert(char ch)
    {
        BeginEditText();
        _text.Insert(CursorCol, ch);
        CursorCol++;
    }

    /// <summary>删除光标前字符（光标 0 时无操作）。</summary>
    public void Backspace()
    {
        BeginEditText();
        if (CursorCol == 0)
        {
            return;
        }

        _text.Remove(CursorCol - 1, 1);
        CursorCol--;
    }

    /// <summary>删除光标处字符（光标在末尾时无操作）。</summary>
    public void Delete()
    {
        BeginEditText();
        if (CursorCol >= _text.Length)
        {
            return;
        }

        _text.Remove(CursorCol, 1);
    }

    /// <summary>光标左移一格（钳制 0）。</summary>
    public void Left()
    {
        BeginEditText();
        CursorCol = Math.Max(0, CursorCol - 1);
    }

    /// <summary>光标右移一格（钳制文本长度）。</summary>
    public void Right()
    {
        BeginEditText();
        CursorCol = Math.Min(_text.Length, CursorCol + 1);
    }

    /// <summary>光标跳到开头。</summary>
    public void Home()
    {
        BeginEditText();
        CursorCol = 0;
    }

    /// <summary>光标跳到末尾。</summary>
    public void End()
    {
        BeginEditText();
        CursorCol = _text.Length;
    }

    /// <summary>清空文本并重置光标。</summary>
    public void Clear()
    {
        BeginEditText();
        _text.Clear();
        CursorCol = 0;
    }

    /// <summary>全量替换文本（历史导航/补全接受用）— cursorToStart=true 光标置行首（基准历史导航语义），否则置末尾。</summary>
    public void Replace(string text, bool cursorToStart = false)
    {
        BeginEditText();
        _text.Clear();
        _text.Append(text);
        CursorCol = cursorToStart ? 0 : text.Length;
    }

    /// <summary>词左移（Alt+B / Ctrl+Left）— 跳到前一个词首：词内回到词首，词首跳过非词字符落前词首；CJK 单字成词。</summary>
    public void WordLeft()
    {
        BeginEditText();
        var pos = CursorCol;
        while (pos > 0 && !IsWordChar(_text[pos - 1]))
        {
            pos--;
        }

        if (pos > 0 && IsWideWordChar(_text[pos - 1]))
        {
            CursorCol = pos - 1;
            return;
        }

        while (pos > 0 && IsWordChar(_text[pos - 1]))
        {
            pos--;
        }

        CursorCol = pos;
    }

    /// <summary>词右移（Alt+F / Ctrl+Right）— 跳到下一个词首：词内跳过本词剩余部分再越过非词字符；CJK 单字成词。</summary>
    public void WordRight()
    {
        BeginEditText();
        var pos = CursorCol;
        if (pos < _text.Length && IsWordChar(_text[pos]))
        {
            if (IsWideWordChar(_text[pos]))
            {
                CursorCol = pos + 1;
                return;
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

        CursorCol = pos;
    }

    /// <summary>剪切到行首（Ctrl+U）— 删除光标前全部文本，prepend 入 kill ring。</summary>
    public void KillToLineStart()
    {
        var killed = _text.ToString(0, CursorCol);
        _text.Remove(0, CursorCol);
        CursorCol = 0;
        KillRing.Push(killed, prepend: true);
    }

    /// <summary>剪切到行尾（Ctrl+K）— 删除光标后全部文本，append 入 kill ring。</summary>
    public void KillToLineEnd()
    {
        var killed = _text.ToString(CursorCol, _text.Length - CursorCol);
        _text.Remove(CursorCol, _text.Length - CursorCol);
        KillRing.Push(killed, prepend: false);
    }

    /// <summary>剪切光标前一个词（Ctrl+W / Alt+Backspace）— 词内剪到词首，prepend 入 kill ring。</summary>
    public void KillWordBefore()
    {
        var target = PrevWordStart(CursorCol);
        var killed = _text.ToString(target, CursorCol - target);
        _text.Remove(target, CursorCol - target);
        CursorCol = target;
        KillRing.Push(killed, prepend: true);
    }

    /// <summary>删除光标后一个词（Alt+D）— 不进 kill ring（对齐基准 deleteWordBefore 直删语义）。</summary>
    public void KillWordAfter()
    {
        BeginEditText();
        var target = NextWordStart(CursorCol);
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

        _text.Remove(start, length);
        _text.Insert(start, text);
        CursorCol = start + text.Length;
        KillRing.UpdateYankLength(text.Length);
        return true;
    }

    /// <summary>打断 kill 累积链（对齐基准非 kill 键 resetKillAccumulation）。</summary>
    public void ResetKillAccumulation() => KillRing.ResetKillAccumulation();

    /// <summary>打断 yank 链（对齐基准非 yank 键 resetYankState）。</summary>
    public void ResetYankState() => KillRing.ResetYankState();

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

    /// <summary>nextWord 起点 — 光标后最近的词首（越过本词剩余部分与非词字符）。</summary>
    private int NextWordStart(int from)
    {
        var pos = from;
        if (pos < _text.Length && IsWordChar(_text[pos]))
        {
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
}
