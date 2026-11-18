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
) {
    /// <summary>
    /// profile 节点 config 内的 metadata 保留命名空间 —— 业务 config 不得占用此前缀。
    /// 放在 config 内而非新增独立段，是为了直接复用 NodeProfileResolver 已验证的
    /// excludes/includes/overrides 继承合并（省略即继承），零新增继承逻辑。
    /// </summary>
    public const string ConfigPrefix = "meta.";

    /// <summary>未声明 metadata 时的唯一默认值 —— 替代此前散落在各构造函数里的硬编码。</summary>
    public static NodeMetadata Unspecified { get; } = new("unspecified", [], [], [], 0, null);

    /// <summary>
    /// 从 profile 节点 config 的 <c>meta.*</c> 保留键构造 metadata。
    /// 没有任何保留键时返回 <see langword="null"/>（该节点未声明 metadata，诊断输出不占位）。
    /// </summary>
    public static NodeMetadata? FromConfig(IReadOnlyDictionary<string, string> config) {
        if (!config.Keys.Any(key => key.StartsWith(ConfigPrefix, StringComparison.Ordinal))) return null;
        // 只解析自描述字段。Priority / EnabledWhenFeatureFlag 属于路由字段，
        // 由 NodeRegistry 从 ResolvedNodeEntry 的权威值填充 —— 避免同一节点出现两个互相矛盾的 priority。
        return new NodeMetadata(
            Version: Get(config, "version") ?? "unspecified",
            Dependencies: SplitList(Get(config, "dependencies")),
            Capabilities: SplitList(Get(config, "capabilities")),
            RequiredCapabilities: SplitList(Get(config, "required-capabilities")),
            Priority: 0,
            EnabledWhenFeatureFlag: null
        );
    }

    private static string? Get(IReadOnlyDictionary<string, string> config, string key) =>
        config.TryGetValue(ConfigPrefix + key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static string[] SplitList(string? raw) =>
        raw is null
            ? []
            : raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
