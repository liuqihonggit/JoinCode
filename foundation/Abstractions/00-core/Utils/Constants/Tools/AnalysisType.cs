namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 代码分析类型枚举
/// [EnumValue] 特性由 EnumMetadataGenerator 自动生成 AnalysisTypeConstants + AnalysisTypeExtensions
/// </summary>
public enum AnalysisType
{
    /// <summary>Bug检测</summary>
    [EnumValue("bugs")] Bugs = 0,

    /// <summary>性能优化</summary>
    [EnumValue("optimize")] Optimize = 1,

    /// <summary>安全审计</summary>
    [EnumValue("security")] Security = 2,

    /// <summary>通用分析</summary>
    [EnumValue("general")] General = 3
}
