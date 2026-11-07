namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 代理隔离模式
/// </summary>
public enum AgentIsolationMode {
    [EnumValue("none")] None,
    [EnumValue("worktree")] Worktree,
    [EnumValue("remote")] Remote
}
