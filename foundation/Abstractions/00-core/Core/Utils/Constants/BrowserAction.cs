namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 浏览器操作类型枚举
/// [EnumValue] 特性由 EnumMetadataGenerator 自动生成 BrowserActionConstants + BrowserActionExtensions
/// </summary>
public enum BrowserAction
{
    /// <summary>打开URL</summary>
    [EnumValue("open")] Open = 0,

    /// <summary>截图</summary>
    [EnumValue("screenshot")] Screenshot = 1,

    /// <summary>执行JavaScript</summary>
    [EnumValue("evaluate")] Evaluate = 2
}
