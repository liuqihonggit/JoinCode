namespace NativeTui.Core.Repl;

/// <summary>
/// OSC 8 超链接发射器 — 对齐基准 utils/hyperlink.ts createHyperlink（序列形态）与
/// utils/markdown.ts:141-161 link 分支（mailto 降级 / 空 URL 纯文本）。
/// 纯函数双成员拆分：<see cref="Wrap"/> 只发射序列（基准 options.supportsHyperlinks
/// 注入式的拆分形态 — 支持性降级由消费方组合 <see cref="IsHyperlinkSupported"/>）；
/// 不含基准 chalk.blue 着色（颜色归 StylePool 主题层，集成时以样式区间叠加）。
/// 序列发射委托 <see cref="Osc.Hyperlink"/>/<see cref="Osc.HyperlinkEnd"/>（既有常量接入消费）。
/// 接线（未决问题，渲染链归主代理）：消费点在 MarkdownLayout/MessageRenderer 输出层。
/// </summary>
public static class HyperlinkEmitter
{
    /// <summary>mailto 协议前缀（基准 startsWith 同 Ordinal 精确匹配 — 区分大小写）。</summary>
    private const string MailtoPrefix = "mailto:";

    /// <summary>
    /// 已知支持 OSC 8 的 TERM_PROGRAM / LC_TERMINAL 值 — 基准 ink/supports-hyperlinks.ts
    /// ADDITIONAL_HYPERLINK_TERMINALS（ghostty/Hyper/kitty/alacritty/iTerm.app/iTerm2）
    /// 合并 supports-hyperlinks 库与 TerminalCapabilities 同类清单常见项（WezTerm/vscode/WarpTerminal/contour）。
    /// </summary>
    private static readonly string[] Osc8CapableTermPrograms =
    [
        "ghostty",
        "Hyper",
        "kitty",
        "alacritty",
        "iTerm.app",
        "iTerm2",
        "WezTerm",
        "vscode",
        "WarpTerminal",
        "contour",
    ];

    /// <summary>探测所需环境变量键白名单（快照与判定共用 — 逐键读取，免全量枚举）。</summary>
    private static readonly string[] ProbedEnvKeys =
    [
        "TERM_PROGRAM",
        "LC_TERMINAL",
        "WT_SESSION",
        "TERM",
        "KITTY_WINDOW_ID",
    ];

    /// <summary>
    /// OSC 8 包裹显示文本 — \x1b]8;;{url}\x1b\\{text}\x1b]8;;\x1b\\（ST 终止）。
    /// mailto: URL 降级为去前缀纯文本邮箱（基准 markdown.ts:143-147 — 显示 href 提取的
    /// email、忽略显示文本，多数终端 mailto 不可点击故展示实际地址更有用）；
    /// url null/空白降级返回纯文本。终端不支持的超链接降级（返回纯 url）由消费方
    /// 组合 <see cref="IsHyperlinkSupported"/> 决定（基准 createHyperlink 内置分支的拆分）。
    /// </summary>
    /// <param name="text">显示文本（链接锚文本，中文/ANSI 载荷原样透传）。</param>
    /// <param name="url">目标 URL；null/空白/mailto: 前缀走降级分支。</param>
    /// <returns>OSC 8 包裹序列，或降级后的纯文本。</returns>
    public static string Wrap(string text, string? url)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (string.IsNullOrWhiteSpace(url))
        {
            return text;
        }

        if (url.StartsWith(MailtoPrefix, StringComparison.Ordinal))
        {
            return url.AsSpan(MailtoPrefix.Length).ToString();
        }

        return Osc.Hyperlink(url) + text + Osc.HyperlinkEnd();
    }

    /// <summary>当前进程环境的 OSC 8 支持探测（每会话判定一次 — 消费方缓存结果）：
    /// env 探测 OR Win32 宿主类名探测（Win10 默认终端=WT 时直接路径启动不注入 WT_SESSION，
    /// 环境探测失效 — 窗口类名 CASCADIA_HOSTING_WINDOW_CLASS 为权威信号，TASK079 用户实测）。</summary>
    /// <returns>是否支持 OSC 8 超链接。</returns>
    public static bool IsHyperlinkSupported() =>
        IsHyperlinkSupported(EnvironmentSnapshot()) || Terminal.TerminalHostDetector.IsWindowsTerminalHosting();

    /// <summary>
    /// OSC 8 支持探测纯函数（env 注入可测，键大小写不敏感 — 跨 Windows/Unix 环境块统一）。
    /// 判定序对齐基准 ink/supports-hyperlinks.ts（+ supports-hyperlinks 库常见项，简单版 —
    /// 库的 stdout 流检测并入环境判定）：① TERM_PROGRAM 白名单 → true；
    /// ② LC_TERMINAL 白名单 → true（tmux 内保留的外层终端标识 — TERM_PROGRAM 被覆盖时兜底）；
    /// ③ WT_SESSION 非空 → true（Windows Terminal）；④ TERM 含 kitty / KITTY_WINDOW_ID 非空 → true；
    /// ⑤ 其余未知 → false（保守 — 不发未声明序列）。
    /// tmux 不排除（基准同 — OSC 8 逐字节透传无损，与 DEC 2026 同步输出的 tmux 排除策略语义不同）。
    /// </summary>
    /// <param name="env">环境变量快照（键→值，值 null 视为未设置）。</param>
    /// <returns>是否支持 OSC 8 超链接。</returns>
    public static bool IsHyperlinkSupported(IReadOnlyDictionary<string, string?> env)
    {
        var vars = Normalize(env);

        var termProgram = vars.GetValueOrDefault("TERM_PROGRAM");
        if (termProgram is not null && Osc8CapableTermPrograms.Contains(termProgram))
        {
            return true;
        }

        // LC_TERMINAL 在 tmux 内保留（iTerm2 等外层终端探测兜底 — 基准分支同译）
        var lcTerminal = vars.GetValueOrDefault("LC_TERMINAL");
        if (lcTerminal is not null && Osc8CapableTermPrograms.Contains(lcTerminal))
        {
            return true;
        }

        // Windows Terminal
        if (IsSet(vars, "WT_SESSION"))
        {
            return true;
        }

        // kitty：TERM 含 kitty 或仅设 KITTY_WINDOW_ID
        return vars.GetValueOrDefault("TERM")?.Contains("kitty") == true || IsSet(vars, "KITTY_WINDOW_ID");
    }

    /// <summary>
    /// OSC 8 探测诊断行（含环境快照 — 便于用户贴报问题上下文，AI 一眼看清终端环境无需另跑命令）。
    /// 首行支持/不支持结论（对齐 IsHyperlinkSupported 判定），后续行 ProbedEnvKeys 键值表
    /// （null→&lt;unset&gt;，空串→&lt;empty&gt; — 排查时区分未设置与显式设空）。
    /// </summary>
    /// <param name="env">环境变量快照（键→值，值 null 视为未设置）。</param>
    /// <returns>多行诊断文本。</returns>
    public static string FormatDiagnostic(IReadOnlyDictionary<string, string?> env)
    {
        var head = IsHyperlinkSupported(env)
            ? "[diag] OSC 8 超链接: 支持（Markdown 链接渲染深链路后续批次接入）"
            : "[diag] OSC 8 超链接: 不支持（终端未声明 — 链接降级纯文本，无需动作）";
        var lines = ProbedEnvKeys
            .Select(k => $"  {k}={FormatEnvValue(env.GetValueOrDefault(k))}")
            .Prepend(head);
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>当前进程环境的 OSC 8 探测诊断行（便利重载 — 会话级调用一次）。</summary>
    /// <returns>多行诊断文本。</returns>
    public static string FormatDiagnostic() => FormatDiagnostic(EnvironmentSnapshot());

    /// <summary>环境变量值格式化（null→&lt;unset&gt;，空串→&lt;empty&gt;，其余原样 — 区分未设置与显式设空）。</summary>
    private static string FormatEnvValue(string? value) => value switch
    {
        null => "<unset>",
        { Length: 0 } => "<empty>",
        _ => value,
    };

    /// <summary>环境快照归一化（键忽略大小写 — 对齐 TerminalCapabilities.Normalize 同语义）。</summary>
    private static Dictionary<string, string?> Normalize(IReadOnlyDictionary<string, string?> env)
    {
        var vars = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in env)
        {
            vars[key] = value;
        }

        return vars;
    }

    /// <summary>环境变量已设置且非空（null/空串视为未设置 — 对齐基准 truthy 判定）。</summary>
    private static bool IsSet(IReadOnlyDictionary<string, string?> vars, string key) =>
        !string.IsNullOrEmpty(vars.GetValueOrDefault(key));

    /// <summary>当前进程环境快照（探测键白名单逐键读取）。</summary>
    private static IReadOnlyDictionary<string, string?> EnvironmentSnapshot()
    {
        var snapshot = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in ProbedEnvKeys)
        {
            snapshot[key] = Environment.GetEnvironmentVariable(key);
        }

        return snapshot;
    }
}
