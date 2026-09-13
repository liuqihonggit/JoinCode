namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 文件修改意图 — 热点识别的双意图基础
/// [EnumValue] 由 EnumMetadataGenerator 自动生成 ModifyIntentConstants + ModifyIntentExtensions
/// </summary>
public enum ModifyIntent
{
    /// <summary>
    /// 内部修改 — 实现细节变更，不改变对外契约（如方法体重构、私有字段调整、注释补充）
    /// 允许多 Worker 并行修改同一文件，不触发热点
    /// </summary>
    [EnumValue("internal")] InternalChange,

    /// <summary>
    /// 契约修改 — 对外签名/接口/公共契约变更（如接口方法增删、公共方法签名改、枚举值改、配置 schema 改）
    /// 触发热点识别，归队长串行收口，Worker 不可并行改
    /// </summary>
    [EnumValue("contract")] ContractChange
}
