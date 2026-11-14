namespace JoinCode.Abstractions.Composition;

/// <summary>
/// 命令分类节点 — 将一个 ChatCommandCategory 下的所有命令包装为 INode
/// </summary>
public sealed class CommandCategoryNode : INode {
    /// <summary>节点唯一标识（如 "session-command"、"model-command"）</summary>
    public string NodeId { get; }

    /// <summary>节点元数据</summary>
    public NodeMetadata Metadata { get; }

    /// <summary>当前生命周期状态</summary>
    public NodeState State { get; internal set; }

    /// <summary>命令分类</summary>
    public ChatCommandCategory Category { get; }

    /// <summary>该分类下的所有命令</summary>
    public IReadOnlyList<IChatCommand> Commands { get; }

    /// <summary>构造命令分类节点</summary>
    /// <param name="nodeId">节点 ID（如 "session-command"）</param>
    /// <param name="category">命令分类</param>
    /// <param name="commands">该分类下的所有命令</param>
    /// <param name="priority">优先级</param>
    public CommandCategoryNode(string nodeId, ChatCommandCategory category, IReadOnlyList<IChatCommand> commands, int priority = 100) {
        ArgumentException.ThrowIfNullOrEmpty(nodeId);
        NodeId = nodeId;
        Category = category;
        Commands = commands;
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
