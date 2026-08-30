namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 命令危险等级枚举 — 统一的命令危险分级，作为权限决策的唯一依据
/// </summary>
/// <remarks>
/// 4级分级语义:
/// <list type="bullet">
/// <item><term>Safe</term><description>只读操作 — 自动通过，无需确认（如 ls/cat/grep/git status）</description></item>
/// <item><term>LightValidation</term><description>轻校验操作 — 绿色 ask，跨信任目录/密钥文件/敏感路径读取（如读取工作目录外文件、.env、.ssh/id_rsa）</description></item>
/// <item><term>Execution</term><description>执行操作 — 红色 ask，git/bash/联网/脚本执行（如 git commit、bash 命令、curl、执行 .sh/.ps1）</description></item>
/// <item><term>Dangerous</term><description>危险操作 — 直接拒绝不提示（如 rm -rf /、format c:、mkfs、fdisk、dd of=/dev/sda）</description></item>
/// </list>
/// ask 级别（LightValidation/Execution）支持"同级别自动通过"标记：用户选择后当前会话内同级别操作不再 ask，不持久化，每次打开新 exe 重新提示。
/// </remarks>
public enum CommandDangerLevel
{
    /// <summary>
    /// 只读操作 — 自动通过，无需确认
    /// </summary>
    [EnumValue("safe")] Safe = 0,

    /// <summary>
    /// 轻校验操作 — 绿色 ask，跨信任目录/密钥文件/敏感路径读取
    /// </summary>
    [EnumValue("light")] LightValidation = 1,

    /// <summary>
    /// 执行操作 — 红色 ask，git/bash/联网/脚本执行
    /// </summary>
    [EnumValue("execution")] Execution = 2,

    /// <summary>
    /// 危险操作 — 直接拒绝不提示
    /// </summary>
    [EnumValue("dangerous")] Dangerous = 3
}
