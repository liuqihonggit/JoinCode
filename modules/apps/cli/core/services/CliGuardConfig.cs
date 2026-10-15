namespace JoinCode;

/// <summary>
/// CLI 守卫超参数配置 — 集中管理 bugfix 守卫的阈值与默认值。
/// <para>每个常量有明确语义：防止错误变量传入、防止超量超长、-1 表示未设置。</para>
/// <para>ADR: 纵深防御按层按名称 — 此类为 CLI 层守卫超参数唯一数据源。</para>
/// </summary>
public static class CliGuardConfig {
    // ── BUG-1: mcp_list 空值防护 ──
    /// <summary>工具描述为空时的默认占位文本</summary>
    public const string DefaultToolDescription = "(无描述)";

    /// <summary>工具名称为空时的默认占位文本</summary>
    public const string DefaultToolName = "(unknown)";

    // ── BUG-2: NonInteractiveExecuteStep 参数范围 ──
    /// <summary>非交互模式 prompt 最大长度（1MB），-1 表示不限制</summary>
    public const int MaxPromptLength = 1_048_576;

    /// <summary>--await 超时下限（秒），1 秒</summary>
    public const int MinAwaitSeconds = 1;

    /// <summary>--await 超时上限（秒），3600 秒 = 1 小时</summary>
    public const int MaxAwaitSeconds = 3600;

    // ── BUG-3: RestoreMsysPathExpansion 路径守卫 ──
    /// <summary>命令行参数数量上限，防止超量参数导致性能问题</summary>
    public const int MaxArgCount = 10_000;

    /// <summary>单个参数长度上限（32KB），防止超长参数</summary>
    public const int MaxArgLength = 32_768;

    /// <summary>Git Bash 路径展开检测的最小长度（如 C:/Git/x 最少 8 字符）</summary>
    public const int MinMsysPathLength = 6;

    // ── BUG-4: CtorScanner 路径守卫 ──
    /// <summary>扫描目录不存在时的退出码</summary>
    public const int ScanDirNotFoundExitCode = 2;

    /// <summary>输出路径最大长度（Windows MAX_PATH 留余量）</summary>
    public const int MaxOutputPathLength = 1024;

    // ── 通用 ──
    /// <summary>-1 表示未设置/不限制</summary>
    public const int Unset = -1;
}
