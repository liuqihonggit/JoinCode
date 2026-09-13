namespace JoinCode.Reasoning.State;

/// <summary>
/// 证据信任度等级
/// </summary>
public enum TrustLevel
{
    /// <summary>直接证据（信任度 100）</summary>
    [EnumValue("100")] DirectEvidence,
    /// <summary>强佐证（信任度 85）</summary>
    [EnumValue("85")] StrongCorroboration,
    /// <summary>中等（信任度 70）</summary>
    [EnumValue("70")] Moderate,
    /// <summary>弱（信任度 50）</summary>
    [EnumValue("50")] Weak,
    /// <summary>传闻（信任度 30）</summary>
    [EnumValue("30")] Hearsay,
    /// <summary>不可靠（信任度 10）</summary>
    [EnumValue("10")] Unreliable,
}
