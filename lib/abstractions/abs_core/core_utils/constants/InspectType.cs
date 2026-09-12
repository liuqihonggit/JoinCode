namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 上下文检查类型枚举
/// [EnumValue] 特性由 EnumMetadataGenerator 自动生成 InspectTypeConstants + InspectTypeExtensions
/// </summary>
public enum InspectType
{
    /// <summary>摘要</summary>
    [EnumValue("summary")] Summary = 0,

    /// <summary>详细信息</summary>
    [EnumValue("detailed")] Detailed = 1,

    /// <summary>分层信息</summary>
    [EnumValue("layers")] Layers = 2
}
