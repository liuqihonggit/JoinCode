namespace JoinCode.Cli.Output;

/// <summary>
/// CLI 模式检测器 — 自动判断 DX（开发者体验）vs AX（Agent 体验）模式
/// 对齐架构指南：检测到 NO_COLOR 或 APP_NO_TUI 时，自动降级为非交互模式
/// </summary>
public enum CliExperienceMode
{
    /// <summary>开发者体验 — 彩色表格、TUI 交互、交互式提示</summary>
    Developer,

    /// <summary>Agent 体验 — 纯 JSON/NDJSON、零着色、不等待输入</summary>
    Agent,
}

/// <summary>
/// CLI 模式检测器 — 根据环境变量和终端状态自动判断 DX/AX 模式
/// </summary>
public static class CliModeDetector
{
    /// <summary>
    /// 检测当前体验模式
    /// 触发 AX 模式的条件（任一满足）：
    /// 1. NO_COLOR 环境变量存在（https://no-color.org/ 标准）
    /// 2. APP_NO_TUI 环境变量存在
    /// 3. JCC_OUTPUT_FORMAT=json 或 ndjson
    /// 4. stdout 被重定向（管道场景）
    /// 5. stdin 被重定向且非 --force-interactive
    /// </summary>
    public static CliExperienceMode DetectMode(bool forceInteractive = false)
    {
        // 1. NO_COLOR 标准 — https://no-color.org/
        if (Environment.GetEnvironmentVariable("NO_COLOR") is not null)
            return CliExperienceMode.Agent;

        // 2. APP_NO_TUI — 禁用 TUI 交互
        if (Environment.GetEnvironmentVariable("APP_NO_TUI") is not null)
            return CliExperienceMode.Agent;

        // 3. JCC_OUTPUT_FORMAT=json/ndjson — 强制结构化输出
        var outputFormat = Environment.GetEnvironmentVariable("JCC_OUTPUT_FORMAT");
        if (string.Equals(outputFormat, "json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(outputFormat, "ndjson", StringComparison.OrdinalIgnoreCase))
            return CliExperienceMode.Agent;

        // 4. stdout 重定向（管道场景）— Agent 通常通过管道消费输出
        if (Console.IsOutputRedirected && !forceInteractive)
            return CliExperienceMode.Agent;

        // 5. stdin 重定向且非强制交互 — Agent 通常通过管道输入
        if (Console.IsInputRedirected && !forceInteractive)
            return CliExperienceMode.Agent;

        return CliExperienceMode.Developer;
    }

    /// <summary>
    /// 是否应禁用颜色输出
    /// 条件：NO_COLOR 存在，或 AX 模式，或 stdout 重定向
    /// </summary>
    public static bool ShouldDisableColor(bool forceInteractive = false) =>
        Environment.GetEnvironmentVariable("NO_COLOR") is not null
        || DetectMode(forceInteractive) == CliExperienceMode.Agent;

    /// <summary>
    /// 是否应禁用 TUI 交互
    /// 条件：APP_NO_TUI 存在，或 AX 模式
    /// </summary>
    public static bool ShouldDisableTui(bool forceInteractive = false) =>
        Environment.GetEnvironmentVariable("APP_NO_TUI") is not null
        || DetectMode(forceInteractive) == CliExperienceMode.Agent;
}
