namespace JoinCode.CliCommands;

/// <summary>
/// CLI 子命令节点 — 一个节点 = 一条入口级子命令（含别名）。
/// <para>
/// TASK100 S4：此前 20 个 <see cref="CliSubCommand"/> 值散落在三处 ——
/// 枚举、<see cref="ApplicationBuilder"/> 的 2 个 <c>if</c>、<see cref="FlatSubCommandRouter"/> 的 16 个 <c>case</c>。
/// 三处各自新增，谁也不知道少没少；命令也无法按环境裁剪。
/// </para>
/// <para>
/// 节点化后：命令表 = 节点表，profile 消费点 <c>cli-commands</c>（contract: command-group）决定启用集合，
/// 路由只按节点表查执行器，不再有 <c>switch</c>。
/// </para>
/// </summary>
public interface ICliCommandNode : Abstractions.Composition.INode {
    /// <summary>该节点服务的规范子命令。</summary>
    CliSubCommand Command { get; }

    /// <summary>指向同一节点的别名字命令（如 <c>rc</c> / <c>remote</c> → <c>remote-control</c>）。</summary>
    IReadOnlyList<CliSubCommand> Aliases { get; }

    /// <summary>执行子命令。</summary>
    /// <param name="args">原始参数（<c>args[0]</c> 为子命令字面量）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>进程退出码。</returns>
    ValueTask<int> ExecuteAsync(string[] args, CancellationToken ct);
}

/// <summary>
/// 子命令节点的公共骨架 — 节点本身无状态，执行全部委托给既有的命令实现（零行为改动）。
/// </summary>
public abstract class CliCommandNodeBase : ICliCommandNode {
    /// <inheritdoc />
    public abstract string NodeId { get; }

    /// <inheritdoc />
    public abstract CliSubCommand Command { get; }

    /// <inheritdoc />
    public virtual IReadOnlyList<CliSubCommand> Aliases => [];

    /// <inheritdoc />
    public Abstractions.Composition.NodeMetadata Metadata => Abstractions.Composition.NodeMetadata.Unspecified;

    /// <inheritdoc />
    public Abstractions.Composition.NodeState State => Abstractions.Composition.NodeState.Active;

    /// <inheritdoc />
    public abstract ValueTask<int> ExecuteAsync(string[] args, CancellationToken ct);
}

/// <summary>
/// CLI 子命令节点清单 — 节点 ID 的唯一命名规则：<see cref="CliSubCommand"/> 枚举**名称** kebab 化。
/// <para>
/// 用枚举名称而不是 <c>[EnumValue]</c>：后者是命令行字面量（<c>mcp_call</c> 带下划线），
/// 拿它当节点 ID 会污染 kebab 约定（同名坑在 S3 角色域已踩过一次）。
/// </para>
/// </summary>
public static class CliCommandNodes {
    /// <summary>命令表面在 profile 中的消费点 ID（contract: command-group）。</summary>
    public const string ConsumerId = "cli-commands";

    /// <summary>
    /// 全部子命令节点。
    /// <para>
    /// 这里 <c>new</c> 出来的实例与 DI 注册的是同类不同例 —— 节点无状态，两者等价；
    /// 静态表供「宿主尚未构建就要路由」的入口使用（子命令分发发生在 DI 容器之前）。
    /// </para>
    /// </summary>
    public static IReadOnlyList<ICliCommandNode> All { get; } = [
        new SchemaCliCommandNode(),
        new ConfigPrintCliCommandNode(),
        new RemoteControlCliCommandNode(),
        new McpCallCliCommandNode(),
        new McpListCliCommandNode(),
        new McpSchemaCliCommandNode(),
        new McpSearchCliCommandNode(),
        new McpServeCliCommandNode(),
        new SlashCallCliCommandNode(),
        new SlashListCliCommandNode(),
        new SlashSchemaCliCommandNode(),
        new RgCliCommandNode(),
        new GhCliCommandNode(),
        new BridgeServeCliCommandNode(),
        new ToolCliCommandNode(),
        new PromptCliCommandNode(),
        new ExperienceCliCommandNode(),
        new ProbeCliCommandNode()
    ];

    /// <summary>全部节点 ID（kebab-case）。</summary>
    public static IReadOnlyList<string> AllNodeIds { get; } = [.. All.Select(static node => node.NodeId)];

    private static readonly Dictionary<CliSubCommand, ICliCommandNode> s_byCommand = BuildIndex();

    /// <summary>按子命令取节点（含别名）。</summary>
    /// <param name="command">子命令枚举值。</param>
    /// <returns>对应节点；未登记时返回 <see langword="null"/>。</returns>
    public static ICliCommandNode? For(CliSubCommand command) =>
        s_byCommand.TryGetValue(command, out var node) ? node : null;

    /// <summary>
    /// 未识别子命令的统一回复 — 路由与节点共用同一句提示，避免两处措辞漂移。
    /// </summary>
    /// <param name="args">原始参数（只用 <c>args[0]</c>）。</param>
    /// <returns>退出码 1。</returns>
    internal static int UnknownSubCommand(string[] args) {
        var literal = args.Length > 0 ? args[0] : "(空)";
        TerminalHelper.WriteError($"未知子命令: {literal}（用 jcc --help 查看可用命令）");
        return 1;
    }

    /// <summary>
    /// 把既有的 <c>Task&lt;int?&gt;</c> 执行器适配成节点契约 —— <see langword="null"/> 表示该实现自认不接这条命令。
    /// </summary>
    /// <param name="task">既有执行器的返回任务。</param>
    /// <param name="args">原始参数（用于兜底提示）。</param>
    /// <returns>退出码。</returns>
    internal static async ValueTask<int> Adapt(Task<int?> task, string[] args) =>
        await task.ConfigureAwait(false) ?? UnknownSubCommand(args);

    private static Dictionary<CliSubCommand, ICliCommandNode> BuildIndex() {
        var index = new Dictionary<CliSubCommand, ICliCommandNode>();
        foreach (var node in All) {
            index[node.Command] = node;
            foreach (var alias in node.Aliases)
                index[alias] = node;
        }
        return index;
    }
}

/// <summary>schema — 输出 CLI 参数定义 JSON（自省）。</summary>
[Register(typeof(SchemaCliCommandNode), ServiceLifetime.Singleton)]
[Register(typeof(ICliCommandNode), ServiceLifetime.Singleton)]
[NodeFactory(CliCommandNodes.ConsumerId, Id)]
public sealed class SchemaCliCommandNode : CliCommandNodeBase {
    /// <summary>节点 ID。</summary>
    public const string Id = "schema";

    /// <inheritdoc />
    public override string NodeId => Id;

    /// <inheritdoc />
    public override CliSubCommand Command => CliSubCommand.Schema;

    /// <inheritdoc />
    public override ValueTask<int> ExecuteAsync(string[] args, CancellationToken ct) =>
        new(ApplicationBuilder.ExecuteSchemaCommand());
}

/// <summary>config-print — 打印合并后的完整配置树。</summary>
[Register(typeof(ConfigPrintCliCommandNode), ServiceLifetime.Singleton)]
[Register(typeof(ICliCommandNode), ServiceLifetime.Singleton)]
[NodeFactory(CliCommandNodes.ConsumerId, Id)]
public sealed class ConfigPrintCliCommandNode : CliCommandNodeBase {
    /// <summary>节点 ID。</summary>
    public const string Id = "config-print";

    /// <inheritdoc />
    public override string NodeId => Id;

    /// <inheritdoc />
    public override CliSubCommand Command => CliSubCommand.ConfigPrint;

    /// <inheritdoc />
    public override ValueTask<int> ExecuteAsync(string[] args, CancellationToken ct) =>
        CliCommandNodes.Adapt(ConfigPrintCommand.ExecuteAsync(args, ct), args);
}

/// <summary>remote-control — 启动远程控制服务（别名 rc / remote）。</summary>
[Register(typeof(RemoteControlCliCommandNode), ServiceLifetime.Singleton)]
[Register(typeof(ICliCommandNode), ServiceLifetime.Singleton)]
[NodeFactory(CliCommandNodes.ConsumerId, Id)]
public sealed class RemoteControlCliCommandNode : CliCommandNodeBase {
    /// <summary>节点 ID。</summary>
    public const string Id = "remote-control";

    /// <inheritdoc />
    public override string NodeId => Id;

    /// <inheritdoc />
    public override CliSubCommand Command => CliSubCommand.RemoteControl;

    /// <inheritdoc />
    public override IReadOnlyList<CliSubCommand> Aliases => [CliSubCommand.Rc, CliSubCommand.Remote];

    /// <inheritdoc />
    public override ValueTask<int> ExecuteAsync(string[] args, CancellationToken ct) =>
        new(ApplicationBuilder.ExecuteRemoteControlAsync(args));
}

/// <summary>mcp-call — MCP 工具直调。</summary>
[Register(typeof(McpCallCliCommandNode), ServiceLifetime.Singleton)]
[Register(typeof(ICliCommandNode), ServiceLifetime.Singleton)]
[NodeFactory(CliCommandNodes.ConsumerId, Id)]
public sealed class McpCallCliCommandNode : CliCommandNodeBase {
    /// <summary>节点 ID。</summary>
    public const string Id = "mcp-call";

    /// <inheritdoc />
    public override string NodeId => Id;

    /// <inheritdoc />
    public override CliSubCommand Command => CliSubCommand.McpCall;

    /// <inheritdoc />
    public override ValueTask<int> ExecuteAsync(string[] args, CancellationToken ct) =>
        CliCommandNodes.Adapt(McpCallCommand.ExecuteAsync(args, ct), args);
}

/// <summary>mcp-list — MCP 工具列表。</summary>
[Register(typeof(McpListCliCommandNode), ServiceLifetime.Singleton)]
[Register(typeof(ICliCommandNode), ServiceLifetime.Singleton)]
[NodeFactory(CliCommandNodes.ConsumerId, Id)]
public sealed class McpListCliCommandNode : CliCommandNodeBase {
    /// <summary>节点 ID。</summary>
    public const string Id = "mcp-list";

    /// <inheritdoc />
    public override string NodeId => Id;

    /// <inheritdoc />
    public override CliSubCommand Command => CliSubCommand.McpList;

    /// <inheritdoc />
    public override ValueTask<int> ExecuteAsync(string[] args, CancellationToken ct) =>
        CliCommandNodes.Adapt(McpListCommand.ExecuteAsync(args, ct), args);
}

/// <summary>mcp-schema — MCP 工具参数 schema。</summary>
[Register(typeof(McpSchemaCliCommandNode), ServiceLifetime.Singleton)]
[Register(typeof(ICliCommandNode), ServiceLifetime.Singleton)]
[NodeFactory(CliCommandNodes.ConsumerId, Id)]
public sealed class McpSchemaCliCommandNode : CliCommandNodeBase {
    /// <summary>节点 ID。</summary>
    public const string Id = "mcp-schema";

    /// <inheritdoc />
    public override string NodeId => Id;

    /// <inheritdoc />
    public override CliSubCommand Command => CliSubCommand.McpSchema;

    /// <inheritdoc />
    public override ValueTask<int> ExecuteAsync(string[] args, CancellationToken ct) =>
        CliCommandNodes.Adapt(McpSchemaCommand.ExecuteAsync(args, ct), args);
}

/// <summary>mcp-search — MCP 工具搜索。</summary>
[Register(typeof(McpSearchCliCommandNode), ServiceLifetime.Singleton)]
[Register(typeof(ICliCommandNode), ServiceLifetime.Singleton)]
[NodeFactory(CliCommandNodes.ConsumerId, Id)]
public sealed class McpSearchCliCommandNode : CliCommandNodeBase {
    /// <summary>节点 ID。</summary>
    public const string Id = "mcp-search";

    /// <inheritdoc />
    public override string NodeId => Id;

    /// <inheritdoc />
    public override CliSubCommand Command => CliSubCommand.McpSearch;

    /// <inheritdoc />
    public override ValueTask<int> ExecuteAsync(string[] args, CancellationToken ct) =>
        CliCommandNodes.Adapt(McpSearchCommand.ExecuteAsync(args, ct), args);
}

/// <summary>mcp-serve — 以 HTTP 服务暴露工具。</summary>
[Register(typeof(McpServeCliCommandNode), ServiceLifetime.Singleton)]
[Register(typeof(ICliCommandNode), ServiceLifetime.Singleton)]
[NodeFactory(CliCommandNodes.ConsumerId, Id)]
public sealed class McpServeCliCommandNode : CliCommandNodeBase {
    /// <summary>节点 ID。</summary>
    public const string Id = "mcp-serve";

    /// <inheritdoc />
    public override string NodeId => Id;

    /// <inheritdoc />
    public override CliSubCommand Command => CliSubCommand.McpServe;

    /// <inheritdoc />
    public override ValueTask<int> ExecuteAsync(string[] args, CancellationToken ct) =>
        CliCommandNodes.Adapt(McpServeCommand.ExecuteAsync(args, ct), args);
}

/// <summary>slash-call — 斜杠命令直调。</summary>
[Register(typeof(SlashCallCliCommandNode), ServiceLifetime.Singleton)]
[Register(typeof(ICliCommandNode), ServiceLifetime.Singleton)]
[NodeFactory(CliCommandNodes.ConsumerId, Id)]
public sealed class SlashCallCliCommandNode : CliCommandNodeBase {
    /// <summary>节点 ID。</summary>
    public const string Id = "slash-call";

    /// <inheritdoc />
    public override string NodeId => Id;

    /// <inheritdoc />
    public override CliSubCommand Command => CliSubCommand.SlashCall;

    /// <inheritdoc />
    public override ValueTask<int> ExecuteAsync(string[] args, CancellationToken ct) =>
        CliCommandNodes.Adapt(SlashCallExecutor.ExecuteAsync(args, ct), args);
}

/// <summary>slash-list — 斜杠命令列表。</summary>
[Register(typeof(SlashListCliCommandNode), ServiceLifetime.Singleton)]
[Register(typeof(ICliCommandNode), ServiceLifetime.Singleton)]
[NodeFactory(CliCommandNodes.ConsumerId, Id)]
public sealed class SlashListCliCommandNode : CliCommandNodeBase {
    /// <summary>节点 ID。</summary>
    public const string Id = "slash-list";

    /// <inheritdoc />
    public override string NodeId => Id;

    /// <inheritdoc />
    public override CliSubCommand Command => CliSubCommand.SlashList;

    /// <inheritdoc />
    public override ValueTask<int> ExecuteAsync(string[] args, CancellationToken ct) =>
        CliCommandNodes.Adapt(SlashListExecutor.ExecuteAsync(args, ct), args);
}

/// <summary>slash-schema — 斜杠命令参数 schema。</summary>
[Register(typeof(SlashSchemaCliCommandNode), ServiceLifetime.Singleton)]
[Register(typeof(ICliCommandNode), ServiceLifetime.Singleton)]
[NodeFactory(CliCommandNodes.ConsumerId, Id)]
public sealed class SlashSchemaCliCommandNode : CliCommandNodeBase {
    /// <summary>节点 ID。</summary>
    public const string Id = "slash-schema";

    /// <inheritdoc />
    public override string NodeId => Id;

    /// <inheritdoc />
    public override CliSubCommand Command => CliSubCommand.SlashSchema;

    /// <inheritdoc />
    public override ValueTask<int> ExecuteAsync(string[] args, CancellationToken ct) =>
        CliCommandNodes.Adapt(SlashSchemaExecutor.ExecuteAsync(args, ct), args);
}

/// <summary>rg — 内置 ripgrep 兼容搜索。</summary>
[Register(typeof(RgCliCommandNode), ServiceLifetime.Singleton)]
[Register(typeof(ICliCommandNode), ServiceLifetime.Singleton)]
[NodeFactory(CliCommandNodes.ConsumerId, Id)]
public sealed class RgCliCommandNode : CliCommandNodeBase {
    /// <summary>节点 ID。</summary>
    public const string Id = "rg";

    /// <inheritdoc />
    public override string NodeId => Id;

    /// <inheritdoc />
    public override CliSubCommand Command => CliSubCommand.Rg;

    /// <inheritdoc />
    public override ValueTask<int> ExecuteAsync(string[] args, CancellationToken ct) =>
        CliCommandNodes.Adapt(RgSubCommand.ExecuteAsync(args, ct), args);
}

/// <summary>gh — GitHub 扁平元动词。</summary>
[Register(typeof(GhCliCommandNode), ServiceLifetime.Singleton)]
[Register(typeof(ICliCommandNode), ServiceLifetime.Singleton)]
[NodeFactory(CliCommandNodes.ConsumerId, Id)]
public sealed class GhCliCommandNode : CliCommandNodeBase {
    /// <summary>节点 ID。</summary>
    public const string Id = "gh";

    /// <inheritdoc />
    public override string NodeId => Id;

    /// <inheritdoc />
    public override CliSubCommand Command => CliSubCommand.Gh;

    /// <inheritdoc />
    public override ValueTask<int> ExecuteAsync(string[] args, CancellationToken ct) =>
        CliCommandNodes.Adapt(GhSubCommand.ExecuteAsync(args, ct), args);
}

/// <summary>bridge-serve — bridge 引擎服务端（stdio NDJSON）。</summary>
[Register(typeof(BridgeServeCliCommandNode), ServiceLifetime.Singleton)]
[Register(typeof(ICliCommandNode), ServiceLifetime.Singleton)]
[NodeFactory(CliCommandNodes.ConsumerId, Id)]
public sealed class BridgeServeCliCommandNode : CliCommandNodeBase {
    /// <summary>节点 ID。</summary>
    public const string Id = "bridge-serve";

    /// <inheritdoc />
    public override string NodeId => Id;

    /// <inheritdoc />
    public override CliSubCommand Command => CliSubCommand.BridgeServe;

    /// <inheritdoc />
    public override ValueTask<int> ExecuteAsync(string[] args, CancellationToken ct) =>
        CliCommandNodes.Adapt(BridgeServeCommand.ExecuteAsync(args, ct), args);
}

/// <summary>tool — CLI 工具节点目录树与搜索。</summary>
[Register(typeof(ToolCliCommandNode), ServiceLifetime.Singleton)]
[Register(typeof(ICliCommandNode), ServiceLifetime.Singleton)]
[NodeFactory(CliCommandNodes.ConsumerId, Id)]
public sealed class ToolCliCommandNode : CliCommandNodeBase {
    /// <summary>节点 ID。</summary>
    public const string Id = "tool";

    /// <inheritdoc />
    public override string NodeId => Id;

    /// <inheritdoc />
    public override CliSubCommand Command => CliSubCommand.Tool;

    /// <inheritdoc />
    public override ValueTask<int> ExecuteAsync(string[] args, CancellationToken ct) =>
        new(CliToolCommand.ExecuteAsync(args, ct));
}

/// <summary>prompt — 系统提示词分节自省。</summary>
[Register(typeof(PromptCliCommandNode), ServiceLifetime.Singleton)]
[Register(typeof(ICliCommandNode), ServiceLifetime.Singleton)]
[NodeFactory(CliCommandNodes.ConsumerId, Id)]
public sealed class PromptCliCommandNode : CliCommandNodeBase {
    /// <summary>节点 ID。</summary>
    public const string Id = "prompt";

    /// <inheritdoc />
    public override string NodeId => Id;

    /// <inheritdoc />
    public override CliSubCommand Command => CliSubCommand.Prompt;

    /// <inheritdoc />
    public override ValueTask<int> ExecuteAsync(string[] args, CancellationToken ct) =>
        new(PromptPrintCommand.ExecuteAsync(args, ct));
}

/// <summary>experience — 经验库 reflect/propose/round。</summary>
[Register(typeof(ExperienceCliCommandNode), ServiceLifetime.Singleton)]
[Register(typeof(ICliCommandNode), ServiceLifetime.Singleton)]
[NodeFactory(CliCommandNodes.ConsumerId, Id)]
public sealed class ExperienceCliCommandNode : CliCommandNodeBase {
    /// <summary>节点 ID。</summary>
    public const string Id = "experience";

    /// <inheritdoc />
    public override string NodeId => Id;

    /// <inheritdoc />
    public override CliSubCommand Command => CliSubCommand.Experience;

    /// <inheritdoc />
    public override ValueTask<int> ExecuteAsync(string[] args, CancellationToken ct) =>
        new(ExperienceCommand.ExecuteAsync(args, ct));
}

/// <summary>probe — 行为对比门禁（TASK098）。</summary>
[Register(typeof(ProbeCliCommandNode), ServiceLifetime.Singleton)]
[Register(typeof(ICliCommandNode), ServiceLifetime.Singleton)]
[NodeFactory(CliCommandNodes.ConsumerId, Id)]
public sealed class ProbeCliCommandNode : CliCommandNodeBase {
    /// <summary>节点 ID。</summary>
    public const string Id = "probe";

    /// <inheritdoc />
    public override string NodeId => Id;

    /// <inheritdoc />
    public override CliSubCommand Command => CliSubCommand.Probe;

    /// <inheritdoc />
    public override ValueTask<int> ExecuteAsync(string[] args, CancellationToken ct) =>
        new(ProbeCommand.ExecuteAsync(args, ct));
}
