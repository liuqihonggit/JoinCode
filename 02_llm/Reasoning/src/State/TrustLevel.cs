namespace JoinCode.Reasoning.State;

/// <summary>
/// 证据信任度等级
/// </summary>
public enum TrustLevel
{
    [EnumValue("100")] DirectEvidence,
    [EnumValue("85")] StrongCorroboration,
    [EnumValue("70")] Moderate,
    [EnumValue("50")] Weak,
    [EnumValue("30")] Hearsay,
    [EnumValue("10")] Unreliable,
}
