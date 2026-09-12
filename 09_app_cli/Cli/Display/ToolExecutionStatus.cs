namespace JoinCode.Cli.Display;

/// <summary>
/// 工具执行状态标记 — CLI 输出 [OK]/[FAIL] 的唯一数据源。
/// 通过 [EnumValue] 源码生成器自动生成 ToValue()/FromValue() 和 ToolExecutionStatusConstants。
/// 消费方禁止硬编码 "OK"/"FAIL" 字符串，统一通过枚举获取。
/// </summary>
public enum ToolExecutionStatus
{
    /// <summary>工具执行成功</summary>
    [EnumValue("OK")] Ok,

    /// <summary>工具执行失败</summary>
    [EnumValue("FAIL")] Fail,
}
