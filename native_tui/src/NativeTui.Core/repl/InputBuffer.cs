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
        _text.Insert(CursorCol, ch);
        CursorCol++;
    }

    /// <summary>删除光标前字符（光标 0 时无操作）。</summary>
    public void Backspace()
    {
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
        if (CursorCol >= _text.Length)
        {
            return;
        }

        _text.Remove(CursorCol, 1);
    }

    /// <summary>光标左移一格（钳制 0）。</summary>
    public void Left() => CursorCol = Math.Max(0, CursorCol - 1);

    /// <summary>光标右移一格（钳制文本长度）。</summary>
    public void Right() => CursorCol = Math.Min(_text.Length, CursorCol + 1);

    /// <summary>光标跳到开头。</summary>
    public void Home() => CursorCol = 0;

    /// <summary>光标跳到末尾。</summary>
    public void End() => CursorCol = _text.Length;

    /// <summary>清空文本并重置光标。</summary>
    public void Clear()
    {
        _text.Clear();
        CursorCol = 0;
    }
}
