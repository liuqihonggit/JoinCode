namespace JoinCode.Abstractions.Composition;

/// <summary>
/// 节点元数据 — 描述节点的版本、依赖、能力和优先级等信息
/// </summary>
public sealed record NodeMetadata(
    string Version,
    string[] Dependencies,
    string[] Capabilities,
    string[] RequiredCapabilities,
    int Priority,
    string? EnabledWhenFeatureFlag
);
