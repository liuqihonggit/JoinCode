namespace JoinCode.Composition.NodeRegistry.Consumers;

/// <summary>
/// 提示词分组节点 — 将 PromptSectionRegistration 的一个分组（Always/AgentMode/CoordinatorMode）包装为 INode
/// </summary>
public sealed class PromptSectionCategoryNode : INode {
    /// <summary>节点唯一标识（如 "always-prompt"、"agent-mode-prompt"、"coordinator-mode-prompt"）</summary>
    public string NodeId { get; }

    /// <summary>节点元数据</summary>
    public NodeMetadata Metadata { get; }

    /// <summary>当前生命周期状态</summary>
    public NodeState State { get; internal set; }

    /// <summary>提示词分组类型</summary>
    public PromptSectionGroup Group { get; }

    /// <summary>该分组下的所有提示词 section</summary>
    public IReadOnlyList<SystemPromptSection> Sections { get; }

    /// <summary>构造提示词分组节点</summary>
    /// <param name="nodeId">节点 ID（如 "always-prompt"）</param>
    /// <param name="group">提示词分组类型</param>
    /// <param name="sections">该分组下的所有 section</param>
    /// <param name="priority">优先级</param>
    public PromptSectionCategoryNode(string nodeId, PromptSectionGroup group, IReadOnlyList<SystemPromptSection> sections, int priority = 100) {
        ArgumentException.ThrowIfNullOrEmpty(nodeId);
        NodeId = nodeId;
        Group = group;
        Sections = sections;
        State = NodeState.Active;
        Metadata = new NodeMetadata(
            Version: "1.0",
            Dependencies: [],
            Capabilities: [],
            RequiredCapabilities: [],
            Priority: priority,
            EnabledWhenFeatureFlag: null
        );
    }
}

/// <summary>
/// 提示词分组类型 — 对应 PromptSectionRegistration 的三个分组
/// </summary>
public enum PromptSectionGroup {
    /// <summary>始终注入的提示词</summary>
    Always,

    /// <summary>Agent 模式下注入的提示词</summary>
    AgentMode,

    /// <summary>Coordinator 模式下注入的提示词</summary>
    CoordinatorMode,
}
