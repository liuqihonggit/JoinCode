namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 会话活动原因枚举
/// </summary>
public enum SessionActivityReason
{
    [EnumValue("apiCall")] ApiCall,
    [EnumValue("toolExecution")] ToolExecution,
    [EnumValue("taskProcessing")] TaskProcessing
}
