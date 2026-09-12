namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 命令危险等级枚举 — 统一的命令危险分级，作为权限决策的唯一依据
/// </summary>
/// <remarks>
/// 5级分级语义（白/绿/黄/红/黑灯）:
/// <list type="bullet">
/// <item><term>Safe（白灯）</term><description>只读操作 — 自动通过，无需确认（如 ls/cat/grep/git status）</description></item>
/// <item><term>Unknown（黄灯）</term><description>未知命令 — 黄灯ask，未在 catalog 中登记的命令需用户确认（如用户自定义脚本 ./exploit.sh）</description></item>
/// <item><term>LightValidation（绿灯）</term><description>可撤回操作 — 绿灯ask，跨信任目录/密钥文件/敏感路径读取或可撤回写入（如 git commit、读取 .env）</description></item>
/// <item><term>Execution（红灯）</term><description>执行/不可撤回操作 — 红灯ask，git/bash/联网/脚本执行（如 rm、curl、git push）</description></item>
/// <item><term>Dangerous（黑灯）</term><description>危险操作 — 直接拒绝不提示（如 rm -rf /、format c:、mkfs、fdisk、dd of=/dev/sda）</description></item>
/// </list>
/// ask 级别（Unknown/LightValidation/Execution）支持"同级别自动通过"标记：用户选择后当前会话内同级别操作不再 ask，不持久化，每次打开新 exe 重新提示。
/// </remarks>
public enum CommandDangerLevel
{
    /// <summary>
    /// 只读操作（白灯）— 自动通过，无需确认
    /// </summary>
    [EnumValue("safe")] Safe = 0,

    /// <summary>
    /// 未知命令（黄灯）— 黄灯ask，未在 catalog 中登记的命令需用户确认
    /// </summary>
    [EnumValue("unknown")] Unknown = 1,

    /// <summary>
    /// 可撤回操作（绿灯）— 绿灯ask，跨信任目录/密钥文件/敏感路径读取或可撤回写入
    /// </summary>
    [EnumValue("light")] LightValidation = 2,

    /// <summary>
    /// 执行/不可撤回操作（红灯）— 红灯ask，git/bash/联网/脚本执行
    /// </summary>
    [EnumValue("execution")] Execution = 3,

    /// <summary>
    /// 危险操作（黑灯）— 直接拒绝不提示
    /// </summary>
    [EnumValue("dangerous")] Dangerous = 4
}

/// <summary>
/// CommandDangerLevel 确认提示解析扩展 — 从确认提示文本解析危险等级（用于确认处理器联动同级别自动通过）
/// </summary>
public static class DangerLevelPromptParser
{
    /// <summary>
    /// 从确认提示文本解析危险等级 — 中间件在 prompt 中嵌入 [黄灯ask]/[绿灯ask]/[红灯ask] 标签
    /// </summary>
    /// <param name="prompt">确认提示文本</param>
    /// <returns>解析到的等级；未包含标签时返回 null</returns>
    public static CommandDangerLevel? ParseLevelFromPrompt(string? prompt)
    {
        if (string.IsNullOrEmpty(prompt))
            return null;

        if (prompt.Contains("[黄灯ask]", StringComparison.Ordinal))
            return CommandDangerLevel.Unknown;
        if (prompt.Contains("[绿灯ask]", StringComparison.Ordinal))
            return CommandDangerLevel.LightValidation;
        if (prompt.Contains("[红灯ask]", StringComparison.Ordinal))
            return CommandDangerLevel.Execution;

        return null;
    }
}
