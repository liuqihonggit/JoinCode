namespace NativeTui.Core.Screen;

/// <summary>
/// 样式 intern 池 — 对齐基准 ink/screen.ts 的 StylePool。
/// 职责：① TextStyle ↔ styleId 双向映射（同样式只存一份）；
/// ② Transition(from, to) 生成最优 SGR diff 序列并缓存（帧间只发变化部分）。
/// 修饰符输出顺序对齐基准 colorize.ts applyTextStyles：
/// inverse → strikethrough → underline → italic → bold → dim → fg → bg。
/// </summary>
public sealed class StylePool
{
    private readonly Dictionary<TextStyle, int> _ids = [];
    private readonly List<TextStyle> _styles = [];
    private readonly Dictionary<(int From, int To), string> _transitions = [];

    /// <summary>创建池并预注册默认样式为 id 0。</summary>
    public StylePool()
    {
        _ids[TextStyle.Default] = 0;
        _styles.Add(TextStyle.Default);
    }

    /// <summary>注册或获取样式的 intern id。</summary>
    public int GetOrAdd(TextStyle style)
    {
        if (_ids.TryGetValue(style, out var id))
        {
            return id;
        }

        id = _styles.Count;
        _ids[style] = id;
        _styles.Add(style);
        return id;
    }

    /// <summary>按 id 取回样式。</summary>
    public TextStyle Get(int id) => _styles[id];

    /// <summary>
    /// 生成从样式 from 到 to 的 SGR 转移序列（只输出差异，结果缓存复用）。
    /// 相同样式返回空字符串。
    /// </summary>
    public string Transition(int from, int to)
    {
        if (from == to)
        {
            return string.Empty;
        }

        var key = (from, to);
        if (_transitions.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var sequence = BuildTransition(_styles[from], _styles[to]);
        _transitions[key] = sequence;
        return sequence;
    }

    private static string BuildTransition(TextStyle from, TextStyle to)
    {
        var builder = new System.Text.StringBuilder();

        if (from.Inverse != to.Inverse)
        {
            builder.Append(to.Inverse ? Sgr.Inverse : Sgr.ResetInverse);
        }

        if (from.Strikethrough != to.Strikethrough)
        {
            builder.Append(to.Strikethrough ? "\x1b[9m" : "\x1b[29m");
        }

        if (from.Underline != to.Underline)
        {
            builder.Append(to.Underline ? Sgr.Underline : Sgr.ResetUnderline);
        }

        if (from.Italic != to.Italic)
        {
            builder.Append(to.Italic ? Sgr.Italic : "\x1b[23m");
        }

        if (from.Bold != to.Bold)
        {
            builder.Append(to.Bold ? Sgr.Bold : Sgr.ResetBold);
        }

        if (from.Dim != to.Dim)
        {
            builder.Append(to.Dim ? Sgr.Dim : "\x1b[22m");
        }

        if (from.Foreground != to.Foreground)
        {
            builder.Append(ColorSequence(to.Foreground, isForeground: true));
        }

        if (from.Background != to.Background)
        {
            builder.Append(ColorSequence(to.Background, isForeground: false));
        }

        return builder.ToString();
    }

    private static string ColorSequence(Color color, bool isForeground) => color.Kind switch
    {
        ColorKind.Default => isForeground ? Sgr.DefaultForeground : Sgr.DefaultBackground,
        ColorKind.Rgb => isForeground
            ? Sgr.Foreground(color.R, color.G, color.B)
            : Sgr.Background(color.R, color.G, color.B),
        ColorKind.Ansi256 => isForeground
            ? Sgr.ForegroundAnsi256(color.Index)
            : Sgr.BackgroundAnsi256(color.Index),
        _ => string.Empty,
    };
}
