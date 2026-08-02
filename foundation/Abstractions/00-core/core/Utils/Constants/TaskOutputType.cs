namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 任务输出类型枚举
/// [EnumValue] 特性由 EnumMetadataGenerator 自动生成 TaskOutputTypeConstants + TaskOutputTypeExtensions
/// </summary>
public enum TaskOutputType
{
    /// <summary>标准输出</summary>
    [EnumValue("stdout")] Stdout = 0,

    /// <summary>标准错误</summary>
    [EnumValue("stderr")] Stderr = 1,

    /// <summary>全部输出</summary>
    [EnumValue("all")] All = 2
}
