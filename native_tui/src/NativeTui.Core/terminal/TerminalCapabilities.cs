namespace NativeTui.Core.Terminal;

/// <summary>
/// 终端能力探测 — 对齐基准 ink/terminal.ts isSynchronizedOutputSupported（DEC 2026 同步输出）：
/// tmux 排除（逐字节透传已破坏原子性）/ TERM_PROGRAM 白名单 / kitty / xterm-ghostty /
/// foot* / alacritty / ZED_TERM / WT_SESSION / VTE_VERSION ≥ 6800；未知终端保守 false。
/// 探测核心为环境字典注入纯函数（测试确定性驱动，脱离宿主终端）；
/// DetectSpinnerFrames 统一委托 <see cref="SpinnerCharsets"/>（Host/ReplShell 单一能力入口）。
/// 接线（未决问题，ReplShell/Host 非本域）：FrameComposer 构造传探测结果
/// （inline 路径 Csi.SynchronizedOutputBegin/End 直拼处同理加条件）、spinner frames 注入。
/// </summary>
public static class TerminalCapabilities
{
    /// <summary>已知支持 DEC 2026 的 TERM_PROGRAM 值（基准白名单同序）。</summary>
    private static readonly string[] SyncCapableTermPrograms =
    [
        "iTerm.app",
        "WezTerm",
        "WarpTerminal",
        "ghostty",
        "contour",
        "vscode",
        "alacritty",
    ];

    /// <summary>DEC 2026 同步输出支持判定阈值（VTE 0.68 起支持 — 基准 6800）。</summary>
    private const int MinimumVteVersion = 6800;

    /// <summary>探测所需环境变量键白名单（快照与判定共用 — 逐键 GetEnvironmentVariable，免全量枚举）。</summary>
    private static readonly string[] ProbedEnvKeys =
    [
        "TMUX",
        "TERM_PROGRAM",
        "TERM",
        "KITTY_WINDOW_ID",
        "ZED_TERM",
        "WT_SESSION",
        "VTE_VERSION",
    ];

    /// <summary>当前进程环境的 DEC 2026 支持探测（每会话一次 — 构造 FrameComposer 时消费）。</summary>
    /// <returns>支持 true（帧输出 BSU/ESU 原子包裹）；不支持 false（裸差分直出）。</returns>
    public static bool IsSynchronizedOutputSupported() =>
        IsSynchronizedOutputSupported(EnvironmentSnapshot());

    /// <summary>
    /// DEC 2026 支持探测纯函数（env 注入可测，键大小写不敏感 — 跨 Windows/Unix 环境块统一）。
    /// 判定序对齐基准：① TMUX 非空 → false（透传分块破坏原子性，包裹徒耗 16 字节/帧）；
    /// ② TERM_PROGRAM 白名单 → true；③ TERM 含 kitty / KITTY_WINDOW_ID → true；
    /// ④ TERM = xterm-ghostty → true；⑤ TERM 以 foot 开头 → true；
    /// ⑥ TERM 含 alacritty → true；⑦ ZED_TERM 非空 → true；⑧ WT_SESSION 非空 → true；
    /// ⑨ VTE_VERSION 数值 ≥ 6800 → true；⑩ 其余未知终端 → false（保守 — 不发未声明的序列）。
    /// </summary>
    /// <param name="env">环境变量快照（键→值，值 null 视为未设置）。</param>
    /// <returns>是否支持 DEC 2026 同步输出。</returns>
    public static bool IsSynchronizedOutputSupported(IReadOnlyDictionary<string, string?> env)
    {
        var vars = Normalize(env);

        // tmux 解析并转发每个字节但不实现 DEC 2026 —— BSU/ESU 透传到外层终端时
        // tmux 已按块切分破坏原子性（基准注释同译）
        if (IsSet(vars, "TMUX"))
        {
            return false;
        }

        var termProgram = vars.GetValueOrDefault("TERM_PROGRAM");
        if (termProgram is not null && SyncCapableTermPrograms.Contains(termProgram))
        {
            return true;
        }

        var term = vars.GetValueOrDefault("TERM");
        if (term?.Contains("kitty") == true || IsSet(vars, "KITTY_WINDOW_ID"))
        {
            return true;
        }

        // ghostty 可能只设 TERM=xterm-ghostty 而无 TERM_PROGRAM（基准分支）
        if (term == "xterm-ghostty")
        {
            return true;
        }

        // foot 设 TERM=foot 或 TERM=foot-extra（基准 StartsWith 分支）
        if (term?.StartsWith("foot") == true)
        {
            return true;
        }

        // alacritty 的 TERM 可能仅含 alacritty 子串（基准 Contains 分支）
        if (term?.Contains("alacritty") == true)
        {
            return true;
        }

        // Zed 内嵌 alacritty_terminal crate（支持 DEC 2026）
        if (IsSet(vars, "ZED_TERM"))
        {
            return true;
        }

        // Windows Terminal
        if (IsSet(vars, "WT_SESSION"))
        {
            return true;
        }

        // VTE 系终端（GNOME Terminal/Tilix 等）0.68+（版本号 ×100）
        return int.TryParse(vars.GetValueOrDefault("VTE_VERSION"), out var vteVersion)
            && vteVersion >= MinimumVteVersion;
    }

    /// <summary>
    /// 当前终端 spinner 12 帧序列 — 统一能力入口委托 <see cref="SpinnerCharsets.Detect"/>
    /// （TERM + IsMacOS 探测，进程内缓存一次；分支序 darwin &gt; ghostty &gt; 默认对齐基准）。
    /// Host/ReplShell 接线（未决问题）：SpinnerRow.Render 的 frames 参数传此返回值。
    /// </summary>
    /// <returns>12 帧往返弹跳字符序列。</returns>
    public static string[] DetectSpinnerFrames() => SpinnerCharsets.Detect();

    /// <summary>环境快照归一化（键忽略大小写 — Windows 环境块不区分大小写，Unix 区分，统一语义）。</summary>
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
