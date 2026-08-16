namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 跨插件资源引用记录 — 插件B 的资源 引用 插件A 的资源
/// <para>用于引用图管理:连带卸载时遍历引用方,通知放弃引用</para>
/// </summary>
public sealed record ResourceReference(
    ObjectId ConsumerResourceId,
    ObjectId TargetResourceId,
    string ConsumerPluginName,
    string TargetPluginName);
