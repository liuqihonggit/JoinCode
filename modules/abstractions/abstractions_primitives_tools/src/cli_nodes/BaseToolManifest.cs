namespace JoinCode.Abstractions.Tools;

/// <summary>
/// base.yaml 根节点 — 基础工具（读/写/编译/bash）的 YAML 注册清单。
/// 由 YamlStream 手动解析，AOT 兼容，不依赖反射式反序列化。
/// </summary>
public sealed record BaseToolManifest {
    /// <summary>清单格式版本。</summary>
    public string Version { get; init; } = "1";

    /// <summary>基础工具列表 — 这是首轮请求 tools 字段的唯一来源。</summary>
    public CliToolDescriptor[] Tools { get; init; } = [];
}
