namespace NativeTui.Core.Repl.Keybindings;

/// <summary>
/// 键序列（chord）— 空格分隔的先后按键序列（"ctrl+x ctrl+e" 二段式），对齐基准 keybindings/parser.ts parseChord。
/// 存储为 <see cref="KeyShortcut"/> 数组：首个键挂起等待第二个（ReplShell _ctrlXPrefix 机制的配置化数据源）。
/// 匹配语义对齐基准 resolver.ts：单键绑定才参与单键匹配（chord.length === 1 才 Matches，Phase 1 跳过多键）；
/// 序列键走 <see cref="IsPrefix(KeyPress)"/>（挂起判定）+ <see cref="MatchesSequence(ReadOnlySpan{KeyPress})"/>（完整触发判定）。
/// </summary>
/// <param name="Steps">先后按键步（长度 ≥ 1；长度 1 = 单键绑定，长度 &gt; 1 = 序列键）。</param>
public sealed record KeyChord(KeyShortcut[] Steps)
{
    /// <summary>是否为多键序列（"ctrl+x ctrl+e"）— 单键绑定为 false。</summary>
    public bool IsSequence => Steps.Length > 1;

    /// <summary>
    /// 解析键序列串。失败抛 <see cref="FormatException"/>（错误文本含段定位与四要素）— 默认表等常量场景用；
    /// 用户输入走 <see cref="TryParse(string, out KeyChord, out string)"/>。
    /// </summary>
    /// <param name="text">键序列串（空格分隔）。</param>
    /// <exception cref="FormatException">任一段非法或整体为空。</exception>
    public static KeyChord Parse(string text) =>
        TryParse(text, out var chord, out var error) ? chord! : throw new FormatException(error);

    /// <summary>
    /// 尝试解析键序列串：整串就是单个空格 → space 键（基准 parser.ts:81-83 quirk：空格是序列分隔符的歧义消解）；
    /// 其余按空白切分为段，逐段 <see cref="KeyShortcut.TryParse(string, out KeyShortcut, out string)"/>。
    /// </summary>
    /// <param name="text">键序列串。</param>
    /// <param name="chord">解析结果（失败时为 null）。</param>
    /// <param name="error">失败原因（四要素 + 第几段定位）。</param>
    /// <returns>解析是否成功。</returns>
    public static bool TryParse(string text, out KeyChord? chord, out string error)
    {
        if (text == " ")
        {
            // 基准 parseChord quirk：孤立空格字符串 = space 键绑定（空格本身不可作分隔符场景）
            chord = new KeyChord([KeyShortcut.Parse("space")]);
            error = string.Empty;
            return true;
        }

        var segments = text.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            chord = null;
            error = "键位串为空或全为空白（KeyChord 解析层拦截）：序列键以空格分隔先后（如 \"ctrl+x ctrl+e\"），单键直接写键名（如 \"ctrl+g\"）。正确写法示例：\"ctrl+g\" 或 \"ctrl+x ctrl+e\"";
            return false;
        }

        var steps = new KeyShortcut[segments.Length];
        for (var i = 0; i < segments.Length; i++)
        {
            if (!KeyShortcut.TryParse(segments[i], out var step, out var stepError))
            {
                chord = null;
                error = $"序列第 {i + 1} 段 \"{segments[i]}\"：{stepError}";
                return false;
            }

            steps[i] = step;
        }

        chord = new KeyChord(steps);
        error = string.Empty;
        return true;
    }

    /// <summary>单键匹配 — 仅单键绑定参与（序列键不拦截单键事件，对齐基准 resolver.ts Phase 1 跳过多键 chord）。</summary>
    /// <param name="key">当前按键。</param>
    /// <returns>单键绑定且命中。</returns>
    public bool Matches(KeyPress key) => Steps.Length == 1 && Steps[0].Matches(key);

    /// <summary>序列首键判定（挂起等待判定）— 该按键是本序列第一步且本序列是多键序列。</summary>
    /// <param name="key">当前按键。</param>
    /// <returns>可作本序列的挂起首键。</returns>
    public bool IsPrefix(KeyPress key) => IsPrefixOf([key]);

    /// <summary>严格前缀判定 — 已按序列是否为本序列的真前缀（长度小于 Steps 且逐位命中；对齐基准 resolver.ts chordPrefixMatches）。</summary>
    /// <param name="pressed">已按下的按键序列。</param>
    /// <returns>是真前缀（还需继续等待后续键）。</returns>
    public bool IsPrefixOf(ReadOnlySpan<KeyPress> pressed)
    {
        if (pressed.Length == 0 || pressed.Length >= Steps.Length)
        {
            return false;
        }

        for (var i = 0; i < pressed.Length; i++)
        {
            if (!Steps[i].Matches(pressed[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>完整序列判定 — 已按序列与本序列等长且逐位命中（触发判定，对齐基准 resolver.ts chordExactlyMatches）。</summary>
    /// <param name="pressed">已按下的完整按键序列。</param>
    /// <returns>序列完整命中。</returns>
    public bool MatchesSequence(ReadOnlySpan<KeyPress> pressed)
    {
        if (pressed.Length != Steps.Length)
        {
            return false;
        }

        for (var i = 0; i < pressed.Length; i++)
        {
            if (!Steps[i].Matches(pressed[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>反向序列化（"ctrl+x ctrl+e"）— 与 <see cref="Parse(string)"/> 往返一致。</summary>
    /// <returns>空格连接的键序列串。</returns>
    public override string ToString() => string.Join(" ", Steps.Select(step => step.ToString()));
}
