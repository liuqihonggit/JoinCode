namespace NativeTui.Tests.Terminal;

/// <summary>
/// 最小 ANSI 虚拟终端（测试专用 — 把渲染输出"真的画出来"，用于断言**残影**）：
/// 只实现 InlineComposer 会发出的序列子集 —— CR/LF（底行 LF 追加新行 = 原生 scrollback 增长）、
/// CUU（**钳制在视口顶** — 真终端行为，是"物理失同步"类缺陷的唯一暴露面）、CUF/CUB、CUP（视口内绝对定位）、
/// EL(2K/0K)、ED(2J 清视口 / 3J 清 scrollback)、文本（宽字符占两列）、其余 SGR/OSC/DEC 私有序列跳过。
/// 模型：无限 scrollback —— 物理行列表只增不减，视口 = 末尾 <see cref="Rows"/> 行；
/// 视口之上的行即"已滚入 scrollback、物理不可变"的历史（对齐真实终端语义）。
/// </summary>
public sealed class VirtualTerminal
{
    private readonly List<List<string>> _lines = [NewLine()];
    private int _cols;
    private int _rows;
    private int _row;
    private int _col;

    /// <summary>创建视口为 cols × rows 的虚拟终端（光标在首行行首）。</summary>
    public VirtualTerminal(int cols, int rows)
    {
        _cols = Math.Max(1, cols);
        _rows = Math.Max(1, rows);
    }

    /// <summary>视口列数。</summary>
    public int Cols => _cols;

    /// <summary>视口行数。</summary>
    public int Rows => _rows;

    /// <summary>视口首行的绝对缓冲行号（其上为 scrollback）。</summary>
    public int ViewportStart => Math.Max(0, _lines.Count - _rows);

    /// <summary>缓冲总行数（含 scrollback）。</summary>
    public int BufferHeight => _lines.Count;

    /// <summary>光标绝对缓冲行号。</summary>
    public int CursorRow => _row;

    /// <summary>光标列。</summary>
    public int CursorCol => _col;

    /// <summary>视口尺寸变更（终端 resize — 视口 = 末尾 Rows 行）。</summary>
    public void Resize(int cols, int rows)
    {
        _cols = Math.Max(1, cols);
        _rows = Math.Max(1, rows);
        _row = Math.Clamp(_row, 0, _lines.Count - 1);
        _col = Math.Min(_col, _cols - 1);
    }

    /// <summary>喂入一段 ANSI 流（渲染输出）。</summary>
    public void Feed(string ansi)
    {
        for (var i = 0; i < ansi.Length; i++)
        {
            var ch = ansi[i];
            switch (ch)
            {
                case '\r':
                    _col = 0;
                    break;
                case '\n':
                    AdvanceLine();
                    break;
                case '\x1b':
                    i = HandleEscape(ansi, i);
                    break;
                default:
                    if (char.IsHighSurrogate(ch) && i + 1 < ansi.Length)
                    {
                        Put(new Rune(ch, ansi[i + 1]));
                        i++;
                    }
                    else
                    {
                        Put(new Rune(ch));
                    }

                    break;
            }
        }
    }

    /// <summary>视口文本（末尾 Rows 行，行尾去空白）— 断言"用户实际看到什么"。</summary>
    public string[] Viewport() => Slice(ViewportStart, _rows);

    /// <summary>全缓冲文本（含 scrollback，行尾去空白）。</summary>
    public string[] AllLines() => Slice(0, _lines.Count);

    /// <summary>视口内查找文本返回首个命中行（-1 = 未命中）。</summary>
    public int FindInViewport(string text) => Array.FindIndex(Viewport(), line => line.Contains(text, StringComparison.Ordinal));

    /// <summary>全缓冲（含 scrollback）查找文本返回首个命中行（-1 = 未命中）。</summary>
    public int FindAnywhere(string text) => Array.FindIndex(AllLines(), line => line.Contains(text, StringComparison.Ordinal));

    private string[] Slice(int start, int count)
    {
        var result = new string[Math.Max(0, Math.Min(count, _lines.Count - start))];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = string.Concat(_lines[start + i]).TrimEnd(' ');
        }

        return result;
    }

    private static List<string> NewLine() => [];

    /// <summary>LF — 越过末行时追加新行（真实终端在视口底行 LF 会滚屏；无限 scrollback 模型下等价于追加）。</summary>
    private void AdvanceLine()
    {
        _row++;
        if (_row >= _lines.Count)
        {
            _lines.Add(NewLine());
        }
    }

    /// <summary>写入一个 rune（宽字符占两列，第二列为空串占位）；越右缘先换行（终端自动折行）。</summary>
    private void Put(Rune rune)
    {
        var width = Math.Clamp(RuneWidth.Of(rune), 1, 2);
        if (_col + width > _cols)
        {
            _col = 0;
            AdvanceLine();
        }

        var line = _lines[_row];
        while (line.Count < _col + width)
        {
            line.Add(" ");
        }

        line[_col] = rune.ToString();
        if (width == 2)
        {
            line[_col + 1] = string.Empty;
        }

        _col += width;
    }

    /// <summary>处理 ESC 序列，返回消费到的最后一个字符下标（调用方 i++ 继续）。</summary>
    private int HandleEscape(string ansi, int start)
    {
        if (start + 1 >= ansi.Length)
        {
            return start;
        }

        var kind = ansi[start + 1];
        if (kind == ']')
        {
            // OSC — 到 BEL 或 ST(ESC \) 结束（超链接）
            for (var i = start + 2; i < ansi.Length; i++)
            {
                if (ansi[i] == '\x07')
                {
                    return i;
                }

                if (ansi[i] == '\x1b' && i + 1 < ansi.Length && ansi[i + 1] == '\\')
                {
                    return i + 1;
                }
            }

            return ansi.Length - 1;
        }

        if (kind != '[')
        {
            return start + 1;
        }

        var cursor = start + 2;
        var parameters = new StringBuilder();
        while (cursor < ansi.Length && (char.IsAsciiDigit(ansi[cursor]) || ansi[cursor] is ';' or '?' or ':'))
        {
            parameters.Append(ansi[cursor]);
            cursor++;
        }

        if (cursor >= ansi.Length)
        {
            return ansi.Length - 1;
        }

        ApplyCsi(parameters.ToString(), ansi[cursor]);
        return cursor;
    }

    private void ApplyCsi(string parameters, char final)
    {
        var raw = parameters.TrimStart('?', '>', '!');
        var isPrivate = raw.Length != parameters.Length;
        int Number(int index, int fallback)
        {
            var parts = raw.Split(';');
            return index < parts.Length && int.TryParse(parts[index], out var value) && value > 0 ? value : fallback;
        }

        switch (final)
        {
            case 'A':
                // CUU 在真终端被钳制在视口顶 —— 残影/失同步缺陷正是这里暴露
                _row = Math.Max(ViewportStart, _row - Number(0, 1));
                break;
            case 'B':
                _row = Math.Min(_lines.Count - 1, _row + Number(0, 1));
                break;
            case 'C':
                _col = Math.Min(_cols - 1, _col + Number(0, 1));
                break;
            case 'D':
                _col = Math.Max(0, _col - Number(0, 1));
                break;
            case 'H':
            case 'f':
                var target = ViewportStart + Number(0, 1) - 1;
                while (target >= _lines.Count)
                {
                    _lines.Add(NewLine());
                }

                _row = Math.Clamp(target, 0, _lines.Count - 1);
                _col = Math.Clamp(Number(1, 1) - 1, 0, _cols - 1);
                break;
            case 'J':
                ApplyEraseDisplay(raw);
                break;
            case 'K':
                ApplyEraseLine(raw);
                break;
            default:
                // 'm' SGR / 'h' 'l' 私有模式 / 其他 —— 对字形无影响，跳过
                break;
        }

        _ = isPrivate;
    }

    private void ApplyEraseDisplay(string raw)
    {
        var mode = int.TryParse(raw, out var value) ? value : 0;
        switch (mode)
        {
            case 2:
                for (var row = ViewportStart; row < _lines.Count; row++)
                {
                    _lines[row] = NewLine();
                }

                break;
            case 3:
                // 清 scrollback（保存行）— 视口内容保留，光标随之上移
                var drop = Math.Min(ViewportStart, _lines.Count - 1);
                if (drop > 0)
                {
                    _lines.RemoveRange(0, drop);
                    _row = Math.Max(0, _row - drop);
                }

                break;
            default:
                // 0：清光标到屏尾
                ClearFromCursor(row: true);
                break;
        }
    }

    private void ApplyEraseLine(string raw)
    {
        var mode = int.TryParse(raw, out var value) ? value : 0;
        var line = _lines[_row];
        switch (mode)
        {
            case 2:
                line.Clear();
                break;
            default:
                for (var col = _col; col < line.Count; col++)
                {
                    line[col] = " ";
                }

                break;
        }
    }

    private void ClearFromCursor(bool row)
    {
        var line = _lines[_row];
        for (var col = _col; col < line.Count; col++)
        {
            line[col] = " ";
        }

        if (row)
        {
            for (var r = _row + 1; r < _lines.Count; r++)
            {
                _lines[r] = NewLine();
            }
        }
    }
}
