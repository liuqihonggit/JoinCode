namespace Api.LLM.QueryServices.Nodes;

/// <summary>
/// LLM 协议实现节点 — 一个节点 = 一种 <see cref="ProtocolKind"/> 的实现方式。
/// <para>
/// TASK100 S3：此前供应商注册表在两处各写一遍（<c>ServiceRegistration.EnsureDefaultProvidersRegistered</c>
/// 与 <c>LlmProvidersPlugin.InitializeAsync</c>），同一份 5+1 清单存在两个真相。
/// 抽成节点后只剩 <see cref="LlmProviderNodes.All"/> 一处，两处调用方都从它派生。
/// </para>
/// <para>
/// 为什么是「节点包装」而不是把 QueryService 本身变成 DI 单例：QueryService 按 <c>ProviderConfig</c>
/// 逐次构造（每次请求一份配置），把它变成单例会改动 LLM 构造链这条热路径；包装层则完全不碰。
/// </para>
/// </summary>
public interface IProtocolQueryServiceNode {
    /// <summary>节点 ID（kebab-case，与 profile 声明一致）。</summary>
    string NodeId { get; }

    /// <summary>该节点承载的协议类型。</summary>
    ProtocolKind Kind { get; }

    /// <summary>按供应商配置创建查询服务。</summary>
    /// <param name="config">供应商配置。</param>
    /// <param name="httpClient">HTTP 客户端。</param>
    /// <param name="logger">日志器。</param>
    /// <param name="fileSystem">文件系统抽象。</param>
    /// <param name="resilientExecutor">弹性 HTTP 执行器。</param>
    /// <returns>查询服务实例。</returns>
    IQueryService Create(
        ProviderConfig config,
        HttpClient? httpClient,
        ILogger? logger,
        IFileSystem? fileSystem,
        ResilientHttpExecutor? resilientExecutor);
}

/// <summary>
/// 协议实现节点的唯一清单 — 注册表、profile 校验、节点工厂都从它派生。
/// </summary>
public static class LlmProviderNodes {
    /// <summary>协议实现节点在 profile 中的消费点 ID（contract: provider）。</summary>
    public const string ConsumerId = "llm-providers";

    /// <summary>全部协议实现节点（顺序仅用于稳定输出，注册表按协议查找，与顺序无关）。</summary>
    public static IReadOnlyList<IProtocolQueryServiceNode> All { get; } = [
        new OpenAiCompatibleProviderNode(),
        new AnthropicProviderNode(),
        new AzureProviderNode(),
        new AgnesProviderNode(),
        new ResponsesProviderNode(),
        new JevProviderNode(),
        new HubProviderNode()
    ];

    /// <summary>全部节点 ID。</summary>
    public static IReadOnlyList<string> AllNodeIds { get; } =
        [.. All.Select(static node => node.NodeId)];

    /// <summary>按节点 ID 取协议类型；未知 ID 返回 null。</summary>
    /// <param name="nodeId">节点 ID（kebab-case）。</param>
    /// <returns>协议类型；未知时返回 null。</returns>
    public static ProtocolKind? KindOf(string nodeId) {
        foreach (var node in All) {
            if (string.Equals(node.NodeId, nodeId, StringComparison.OrdinalIgnoreCase))
                return node.Kind;
        }
        return null;
    }
}

/// <summary>
/// OpenAI 兼容协议（默认实现）节点 — <see cref="ProtocolKind.OpenAiCompatible"/>。
/// <para>
/// 这是使用面最广的协议（openai / deepseek / sensenova / agnes 等 vendor 都映射到它），
/// 因此必须是显式节点：漏掉它会让「任何 openai 兼容供应商」在 profile 校验阶段被整体拒绝。
/// </para>
/// </summary>
[Register(typeof(OpenAiCompatibleProviderNode), ServiceLifetime.Singleton)]
[Register(typeof(IProtocolQueryServiceNode), ServiceLifetime.Singleton)]
[NodeFactory(LlmProviderNodes.ConsumerId, "openai-compatible")]
public sealed class OpenAiCompatibleProviderNode : IProtocolQueryServiceNode {
    /// <inheritdoc />
    public string NodeId => "openai-compatible";

    /// <inheritdoc />
    public ProtocolKind Kind => ProtocolKind.OpenAiCompatible;

    /// <inheritdoc />
    public IQueryService Create(ProviderConfig config, HttpClient? httpClient, ILogger? logger, IFileSystem? fileSystem, ResilientHttpExecutor? resilientExecutor)
        => new OpenAIQueryService(config, httpClient, logger, fileSystem, resilientExecutor);
}

/// <summary>Anthropic v1/messages 协议实现节点。</summary>
[Register(typeof(AnthropicProviderNode), ServiceLifetime.Singleton)]
[Register(typeof(IProtocolQueryServiceNode), ServiceLifetime.Singleton)]
[NodeFactory(LlmProviderNodes.ConsumerId, "anthropic")]
public sealed class AnthropicProviderNode : IProtocolQueryServiceNode {
    /// <inheritdoc />
    public string NodeId => "anthropic";

    /// <inheritdoc />
    public ProtocolKind Kind => ProtocolKind.Anthropic;

    /// <inheritdoc />
    public IQueryService Create(ProviderConfig config, HttpClient? httpClient, ILogger? logger, IFileSystem? fileSystem, ResilientHttpExecutor? resilientExecutor)
        => new AnthropicQueryService(config, httpClient, logger, fileSystem, resilientExecutor);
}

/// <summary>Azure OpenAI 协议实现节点。</summary>
[Register(typeof(AzureProviderNode), ServiceLifetime.Singleton)]
[Register(typeof(IProtocolQueryServiceNode), ServiceLifetime.Singleton)]
[NodeFactory(LlmProviderNodes.ConsumerId, "azure")]
public sealed class AzureProviderNode : IProtocolQueryServiceNode {
    /// <inheritdoc />
    public string NodeId => "azure";

    /// <inheritdoc />
    public ProtocolKind Kind => ProtocolKind.Azure;

    /// <inheritdoc />
    public IQueryService Create(ProviderConfig config, HttpClient? httpClient, ILogger? logger, IFileSystem? fileSystem, ResilientHttpExecutor? resilientExecutor)
        => new AzureQueryService(config, httpClient, logger, fileSystem, resilientExecutor);
}

/// <summary>Agnes 协议实现节点。</summary>
[Register(typeof(AgnesProviderNode), ServiceLifetime.Singleton)]
[Register(typeof(IProtocolQueryServiceNode), ServiceLifetime.Singleton)]
[NodeFactory(LlmProviderNodes.ConsumerId, "agnes")]
public sealed class AgnesProviderNode : IProtocolQueryServiceNode {
    /// <inheritdoc />
    public string NodeId => "agnes";

    /// <inheritdoc />
    public ProtocolKind Kind => ProtocolKind.Agnes;

    /// <inheritdoc />
    public IQueryService Create(ProviderConfig config, HttpClient? httpClient, ILogger? logger, IFileSystem? fileSystem, ResilientHttpExecutor? resilientExecutor)
        => new AgnesQueryService(config, httpClient, logger, fileSystem, resilientExecutor);
}

/// <summary>OpenAI Responses API 协议实现节点。</summary>
[Register(typeof(ResponsesProviderNode), ServiceLifetime.Singleton)]
[Register(typeof(IProtocolQueryServiceNode), ServiceLifetime.Singleton)]
[NodeFactory(LlmProviderNodes.ConsumerId, "openai-responses")]
public sealed class ResponsesProviderNode : IProtocolQueryServiceNode {
    /// <inheritdoc />
    public string NodeId => "openai-responses";

    /// <inheritdoc />
    public ProtocolKind Kind => ProtocolKind.OpenAiResponses;

    /// <inheritdoc />
    public IQueryService Create(ProviderConfig config, HttpClient? httpClient, ILogger? logger, IFileSystem? fileSystem, ResilientHttpExecutor? resilientExecutor)
        => new ResponsesQueryService(config, httpClient, logger, fileSystem, resilientExecutor);
}

/// <summary>Jev 协议实现节点。</summary>
[Register(typeof(JevProviderNode), ServiceLifetime.Singleton)]
[Register(typeof(IProtocolQueryServiceNode), ServiceLifetime.Singleton)]
[NodeFactory(LlmProviderNodes.ConsumerId, "jev")]
public sealed class JevProviderNode : IProtocolQueryServiceNode {
    /// <inheritdoc />
    public string NodeId => "jev";

    /// <inheritdoc />
    public ProtocolKind Kind => ProtocolKind.Jev;

    /// <inheritdoc />
    public IQueryService Create(ProviderConfig config, HttpClient? httpClient, ILogger? logger, IFileSystem? fileSystem, ResilientHttpExecutor? resilientExecutor)
        => new JevQueryService(config, httpClient, logger, fileSystem, resilientExecutor);
}

/// <summary>Hub（本地 Pipe 服务）协议实现节点 — 构造参数与其它协议不同，差异封装在节点内。</summary>
[Register(typeof(HubProviderNode), ServiceLifetime.Singleton)]
[Register(typeof(IProtocolQueryServiceNode), ServiceLifetime.Singleton)]
[NodeFactory(LlmProviderNodes.ConsumerId, "hub")]
public sealed class HubProviderNode : IProtocolQueryServiceNode {
    /// <inheritdoc />
    public string NodeId => "hub";

    /// <inheritdoc />
    public ProtocolKind Kind => ProtocolKind.Hub;

    /// <inheritdoc />
    public IQueryService Create(ProviderConfig config, HttpClient? httpClient, ILogger? logger, IFileSystem? fileSystem, ResilientHttpExecutor? resilientExecutor) {
        ArgumentNullException.ThrowIfNull(config);

        return new HubQueryService(
            new HubTransportConfig {
                Endpoint = config.Definition?.DefaultEndpoint ?? "http://localhost:3457",
                ModelId = config.ModelId,
                AgentId = config.AgentId
            },
            config.ApiKey,
            logger as ILogger<HubQueryService>,
            resilientExecutor);
    }
}
