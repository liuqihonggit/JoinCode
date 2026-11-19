namespace JoinCode.Composition.NodeRegistry;

/// <summary>
/// 工具组节点 — 一个节点 = 一个独立工具 exe 组（<c>jcc-tool-&lt;组&gt;.exe</c>）。
/// <para>
/// TASK100 S6：<c>resources/tools/cli_groups.yaml</c> 里的 13 个组（含 388 个不经宿主的隔离工具）
/// 此前是「全量可见」：首轮提示词无条件列出全部组，<c>jcc tool tree/find/help/call</c> 也一律放行。
/// 于是 profile 能关掉宿主的 368 个工具（S4 收尾），却关不掉另外 388 个 —— 而后者对模型同样可见，
/// 因为提示词就是它们的唯一入口（模型靠 bash 调 <c>jcc tool call</c> 使用）。
/// </para>
/// <para>
/// 契约复用 <c>capability</c>：「存在即提供，不互斥，可任意组合」。组与能力在此结构同构 ——
/// 都不需要顺序，只需要回答「在不在」。因此不新增契约值，新增的是消费点 <c>tool-groups</c>。
/// </para>
/// </summary>
public interface IToolGroupNode : INode {
    /// <summary>组摘要（诊断用；工具清单的唯一来源仍是 <c>resources/tools/cli_groups.yaml</c>）。</summary>
    string Summary { get; }
}

/// <summary>
/// 工具组节点清单 — 只负责「有哪些节点实例」。
/// <para>
/// ID 清单与启停判定住在 <see cref="ToolGroupDeclaration"/>（抽象层）：提示词层看不到装配层，
/// 若把 ID 写在这里，提示词就只能靠 <c>cli_groups.yaml</c> 全量渲染，profile 门禁在提示词侧失效。
/// </para>
/// </summary>
public static class ToolGroupNodes {
    /// <summary>工具组在 profile 中的消费点 ID（contract: capability）。</summary>
    public const string ConsumerId = ToolGroupDeclaration.ConsumerId;

    /// <summary>全部工具组节点实例（顺序与 <see cref="ToolGroupDeclaration.AllNodeIds"/> 一致）。</summary>
    public static IReadOnlyList<IToolGroupNode> All { get; } = [
        new AgentToolGroupNode(),
        new CodeToolGroupNode(),
        new FileToolGroupNode(),
        new GitToolGroupNode(),
        new GithubToolGroupNode(),
        new McpToolGroupNode(),
        new MediaToolGroupNode(),
        new SearchToolGroupNode(),
        new ShellToolGroupNode(),
        new SlashToolGroupNode(),
        new SystemToolGroupNode(),
        new ToolsToolGroupNode(),
        new WebToolGroupNode()
    ];
}

/// <summary>工具组节点基类 — 只有一个差异（<see cref="NodeId"/>），其余元数据完全一致。</summary>
public abstract class ToolGroupNodeBase : IToolGroupNode {
    /// <inheritdoc />
    public abstract string NodeId { get; }

    /// <inheritdoc />
    public NodeMetadata Metadata => NodeMetadata.Unspecified;

    /// <inheritdoc />
    public NodeState State => NodeState.Active;

    /// <inheritdoc />
    public string Summary => $"工具组 {NodeId}（exe 与工具清单见 resources/tools/cli_groups.yaml）";
}

/// <summary>子代理与 worktree 管理组节点。</summary>
[Register(typeof(AgentToolGroupNode), ServiceLifetime.Singleton)]
[Register(typeof(IToolGroupNode), ServiceLifetime.Singleton)]
[NodeFactory(ToolGroupNodes.ConsumerId, Id)]
public sealed class AgentToolGroupNode : ToolGroupNodeBase {
    /// <summary>节点 ID。</summary>
    public const string Id = ToolGroupDeclaration.Agent;

    /// <inheritdoc />
    public override string NodeId => Id;
}

/// <summary>notebook 与代码工作流组节点。</summary>
[Register(typeof(CodeToolGroupNode), ServiceLifetime.Singleton)]
[Register(typeof(IToolGroupNode), ServiceLifetime.Singleton)]
[NodeFactory(ToolGroupNodes.ConsumerId, Id)]
public sealed class CodeToolGroupNode : ToolGroupNodeBase {
    /// <summary>节点 ID。</summary>
    public const string Id = ToolGroupDeclaration.Code;

    /// <inheritdoc />
    public override string NodeId => Id;
}

/// <summary>文件读写与编辑组节点。</summary>
[Register(typeof(FileToolGroupNode), ServiceLifetime.Singleton)]
[Register(typeof(IToolGroupNode), ServiceLifetime.Singleton)]
[NodeFactory(ToolGroupNodes.ConsumerId, Id)]
public sealed class FileToolGroupNode : ToolGroupNodeBase {
    /// <summary>节点 ID。</summary>
    public const string Id = ToolGroupDeclaration.File;

    /// <inheritdoc />
    public override string NodeId => Id;
}

/// <summary>git 操作组节点。</summary>
[Register(typeof(GitToolGroupNode), ServiceLifetime.Singleton)]
[Register(typeof(IToolGroupNode), ServiceLifetime.Singleton)]
[NodeFactory(ToolGroupNodes.ConsumerId, Id)]
public sealed class GitToolGroupNode : ToolGroupNodeBase {
    /// <summary>节点 ID。</summary>
    public const string Id = ToolGroupDeclaration.Git;

    /// <inheritdoc />
    public override string NodeId => Id;
}

/// <summary>GitHub 全量操作组节点。</summary>
[Register(typeof(GithubToolGroupNode), ServiceLifetime.Singleton)]
[Register(typeof(IToolGroupNode), ServiceLifetime.Singleton)]
[NodeFactory(ToolGroupNodes.ConsumerId, Id)]
public sealed class GithubToolGroupNode : ToolGroupNodeBase {
    /// <summary>节点 ID。</summary>
    public const string Id = ToolGroupDeclaration.Github;

    /// <inheritdoc />
    public override string NodeId => Id;
}

/// <summary>MCP 连接与配置组节点。</summary>
[Register(typeof(McpToolGroupNode), ServiceLifetime.Singleton)]
[Register(typeof(IToolGroupNode), ServiceLifetime.Singleton)]
[NodeFactory(ToolGroupNodes.ConsumerId, Id)]
public sealed class McpToolGroupNode : ToolGroupNodeBase {
    /// <summary>节点 ID。</summary>
    public const string Id = ToolGroupDeclaration.Mcp;

    /// <inheritdoc />
    public override string NodeId => Id;
}

/// <summary>桌面自动化与图像处理组节点。</summary>
[Register(typeof(MediaToolGroupNode), ServiceLifetime.Singleton)]
[Register(typeof(IToolGroupNode), ServiceLifetime.Singleton)]
[NodeFactory(ToolGroupNodes.ConsumerId, Id)]
public sealed class MediaToolGroupNode : ToolGroupNodeBase {
    /// <summary>节点 ID。</summary>
    public const string Id = ToolGroupDeclaration.Media;

    /// <inheritdoc />
    public override string NodeId => Id;
}

/// <summary>文件与符号搜索组节点。</summary>
[Register(typeof(SearchToolGroupNode), ServiceLifetime.Singleton)]
[Register(typeof(IToolGroupNode), ServiceLifetime.Singleton)]
[NodeFactory(ToolGroupNodes.ConsumerId, Id)]
public sealed class SearchToolGroupNode : ToolGroupNodeBase {
    /// <summary>节点 ID。</summary>
    public const string Id = ToolGroupDeclaration.Search;

    /// <inheritdoc />
    public override string NodeId => Id;
}

/// <summary>shell 命令组节点。</summary>
[Register(typeof(ShellToolGroupNode), ServiceLifetime.Singleton)]
[Register(typeof(IToolGroupNode), ServiceLifetime.Singleton)]
[NodeFactory(ToolGroupNodes.ConsumerId, Id)]
public sealed class ShellToolGroupNode : ToolGroupNodeBase {
    /// <summary>节点 ID。</summary>
    public const string Id = ToolGroupDeclaration.Shell;

    /// <inheritdoc />
    public override string NodeId => Id;
}

/// <summary>斜杠命令组节点。</summary>
[Register(typeof(SlashToolGroupNode), ServiceLifetime.Singleton)]
[Register(typeof(IToolGroupNode), ServiceLifetime.Singleton)]
[NodeFactory(ToolGroupNodes.ConsumerId, Id)]
public sealed class SlashToolGroupNode : ToolGroupNodeBase {
    /// <summary>节点 ID。</summary>
    public const string Id = ToolGroupDeclaration.Slash;

    /// <inheritdoc />
    public override string NodeId => Id;
}

/// <summary>系统与进程组节点。</summary>
[Register(typeof(SystemToolGroupNode), ServiceLifetime.Singleton)]
[Register(typeof(IToolGroupNode), ServiceLifetime.Singleton)]
[NodeFactory(ToolGroupNodes.ConsumerId, Id)]
public sealed class SystemToolGroupNode : ToolGroupNodeBase {
    /// <summary>节点 ID。</summary>
    public const string Id = ToolGroupDeclaration.System;

    /// <inheritdoc />
    public override string NodeId => Id;
}

/// <summary>工具自省与基础工具组节点。</summary>
[Register(typeof(ToolsToolGroupNode), ServiceLifetime.Singleton)]
[Register(typeof(IToolGroupNode), ServiceLifetime.Singleton)]
[NodeFactory(ToolGroupNodes.ConsumerId, Id)]
public sealed class ToolsToolGroupNode : ToolGroupNodeBase {
    /// <summary>节点 ID。</summary>
    public const string Id = ToolGroupDeclaration.Tools;

    /// <inheritdoc />
    public override string NodeId => Id;
}

/// <summary>web 抓取与检索组节点。</summary>
[Register(typeof(WebToolGroupNode), ServiceLifetime.Singleton)]
[Register(typeof(IToolGroupNode), ServiceLifetime.Singleton)]
[NodeFactory(ToolGroupNodes.ConsumerId, Id)]
public sealed class WebToolGroupNode : ToolGroupNodeBase {
    /// <summary>节点 ID。</summary>
    public const string Id = ToolGroupDeclaration.Web;

    /// <inheritdoc />
    public override string NodeId => Id;
}
