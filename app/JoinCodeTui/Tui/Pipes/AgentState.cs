namespace JoinCode.Tui.Pipes;

/// <summary>
/// Agent 运行状态 — 驱动子代理卡片状态图标颜色。
/// </summary>
public enum AgentState
{
    /// <summary>运行中（绿色 ●）</summary>
    [EnumValue("running")] Running,
    /// <summary>等待中（黄色 ●）</summary>
    [EnumValue("waiting")] Waiting,
    /// <summary>已完成（蓝色 ●）</summary>
    [EnumValue("completed")] Completed,
    /// <summary>错误（红色 ●）</summary>
    [EnumValue("error")] Error,
    /// <summary>已停止（灰色 ●）</summary>
    [EnumValue("stopped")] Stopped,
}
