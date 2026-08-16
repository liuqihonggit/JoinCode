namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// UI 资源条目 — 插件持有的单个界面资源
/// </summary>
public sealed record UiResourceEntry(
    string Key,
    UiResourceKind Kind,
    string DisplayName,
    object? Payload);
