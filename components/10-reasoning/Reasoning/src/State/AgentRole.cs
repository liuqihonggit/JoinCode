namespace JoinCode.Reasoning.State;

/// <summary>
/// 三权分立Agent角色
/// </summary>
public enum AgentRole
{
    [EnumValue("prosecutor")] Prosecutor,
    [EnumValue("defender")] Defender,
    [EnumValue("judge")] Judge,
}
