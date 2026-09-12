namespace JoinCode.Cli.Output;

/// <summary>
/// 命令风险分级 — 对齐架构指南安全设计
/// read: 只读，直接执行
/// write: 修改，需确认
/// dangerous: 不可逆，需复核
/// </summary>
public enum CommandRiskLevel
{
    /// <summary>只读操作 — 直接执行，无需确认（如 Read/Grep/Glob/List）</summary>
    Read = 0,

    /// <summary>修改操作 — 需用户确认（如 Write/Edit/Bash(git:*)/McpConnect）</summary>
    Write = 1,

    /// <summary>不可逆操作 — 需复核确认（如 Bash(rm)/Bash(format)/Delete）</summary>
    Dangerous = 2,
}
