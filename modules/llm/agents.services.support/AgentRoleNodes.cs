namespace Core.Agents;

/// <summary>
/// 内置角色节点 — 一个节点 = 一份 <see cref="AgentRoleProfile"/>（role + 可选 variant）。
/// <para>
/// TASK100 S3：9 份内置档案此前只存在于 <c>BuildBuiltInProfiles()</c> 的一个大数组里，
/// 既无法被 profile 单独开关，也不在 <c>--dump-nodes</c> 视野内。
/// 抽成节点后：档案数据仍只写一次（<c>BuildBuiltInProfiles</c> 是唯一来源，节点只是按 ID 取视图），
/// 启停由 profile 消费点 <c>agent-roles</c> 决定。
/// </para>
/// </summary>
public interface IAgentRoleNode {
    /// <summary>节点 ID（kebab-case，如 executor-code）。</summary>
    string NodeId { get; }

    /// <summary>该节点对应的角色档案。</summary>
    AgentRoleProfile Profile { get; }
}

/// <summary>
/// 内置角色节点清单与档案索引 — 节点 ID 由枚举的 <c>[EnumValue]</c> 派生（单一命名来源）。
/// </summary>
public static class AgentRoleNodes {
    /// <summary>角色节点在 profile 中的消费点 ID（contract: role）。</summary>
    public const string ConsumerId = AgentRoleDeclaration.ConsumerId;

    private static readonly Dictionary<string, AgentRoleProfile> s_profileByNodeId = BuildIndex();

    /// <summary>全部角色节点。</summary>
    public static IReadOnlyList<IAgentRoleNode> All { get; } = [
        new CoordinatorRoleNode(),
        new ExecutorCodeRoleNode(),
        new ExecutorSearchRoleNode(),
        new ExecutorExploreRoleNode(),
        new ExecutorPlanRoleNode(),
        new ExecutorDoctorRoleNode(),
        new ExecutorVerificationRoleNode(),
        new ExecutorJoinCodeGuideRoleNode(),
        new ExecutorContextCompressionRoleNode()
    ];

    /// <summary>全部节点 ID。</summary>
    public static IReadOnlyList<string> AllNodeIds { get; } =
        [.. All.Select(static node => node.NodeId)];

    /// <summary>节点 ID → 角色档案。</summary>
    /// <param name="nodeId">节点 ID（kebab-case）。</param>
    /// <returns>角色档案</returns>
    /// <exception cref="InvalidOperationException">节点 ID 在内置档案中不存在。</exception>
    public static AgentRoleProfile ProfileOf(string nodeId) {
        ArgumentException.ThrowIfNullOrEmpty(nodeId);
        return s_profileByNodeId.TryGetValue(nodeId, out var profile)
            ? profile
            : throw new InvalidOperationException(
                $"[AGENT-ROLE-UNKNOWN] 没有节点 ID 为 '{nodeId}' 的内置角色档案。可用: {string.Join(", ", s_profileByNodeId.Keys.Order(StringComparer.Ordinal))}。");
    }

    /// <summary>
    /// 取某份档案的节点 ID — 唯一命名规则：role 的枚举**名称** kebab 化，可选再拼 variant 名称。
    /// <para>
    /// 用名称而不是 <c>[EnumValue]</c>：后者是配置里的序列化值，历史上出现过 camelCase
    /// （<c>claudeCodeGuide</c> / <c>contextCompression</c>），拿它当节点 ID 会破坏 kebab 约定。
    /// </para>
    /// </summary>
    /// <param name="profile">角色档案</param>
    /// <returns>节点 ID</returns>
    internal static string NodeIdOf(AgentRoleProfile profile) {
        ArgumentNullException.ThrowIfNull(profile);
        var role = ToKebab(profile.Role.ToString());
        return profile.Variant is { } variant ? $"{role}-{ToKebab(variant.ToString())}" : role;
    }

    /// <summary>PascalCase → kebab-case（JoinCodeGuide → join-code-guide）。</summary>
    /// <param name="pascal">PascalCase 名称</param>
    /// <returns>kebab-case 名称</returns>
    private static string ToKebab(string pascal) {
        var sb = new StringBuilder(pascal.Length + 4);
        for (var i = 0; i < pascal.Length; i++) {
            var ch = pascal[i];
            if (char.IsUpper(ch) && i > 0)
                sb.Append('-');
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    private static Dictionary<string, AgentRoleProfile> BuildIndex() {
        var index = new Dictionary<string, AgentRoleProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in AgentRoleProfileRegistry.BuildBuiltInProfiles()) {
            index[NodeIdOf(profile)] = profile;
        }
        return index;
    }
}

/// <summary>协调者角色节点。</summary>
[Register(typeof(CoordinatorRoleNode), ServiceLifetime.Singleton)]
[Register(typeof(IAgentRoleNode), ServiceLifetime.Singleton)]
[NodeFactory(AgentRoleNodes.ConsumerId, Id)]
public sealed class CoordinatorRoleNode : IAgentRoleNode {
    /// <summary>节点 ID 常量 — 同时供特性与属性使用，避免两处字面量漂移。</summary>
    public const string Id = "coordinator";

    /// <inheritdoc />
    public string NodeId => Id;

    /// <inheritdoc />
    public AgentRoleProfile Profile => AgentRoleNodes.ProfileOf(Id);
}

/// <summary>代码执行者角色节点。</summary>
[Register(typeof(ExecutorCodeRoleNode), ServiceLifetime.Singleton)]
[Register(typeof(IAgentRoleNode), ServiceLifetime.Singleton)]
[NodeFactory(AgentRoleNodes.ConsumerId, Id)]
public sealed class ExecutorCodeRoleNode : IAgentRoleNode {
    /// <summary>节点 ID 常量。</summary>
    public const string Id = "executor-code";

    /// <inheritdoc />
    public string NodeId => Id;

    /// <inheritdoc />
    public AgentRoleProfile Profile => AgentRoleNodes.ProfileOf(Id);
}

/// <summary>搜索执行者角色节点。</summary>
[Register(typeof(ExecutorSearchRoleNode), ServiceLifetime.Singleton)]
[Register(typeof(IAgentRoleNode), ServiceLifetime.Singleton)]
[NodeFactory(AgentRoleNodes.ConsumerId, Id)]
public sealed class ExecutorSearchRoleNode : IAgentRoleNode {
    /// <summary>节点 ID 常量。</summary>
    public const string Id = "executor-search";

    /// <inheritdoc />
    public string NodeId => Id;

    /// <inheritdoc />
    public AgentRoleProfile Profile => AgentRoleNodes.ProfileOf(Id);
}

/// <summary>探索执行者角色节点。</summary>
[Register(typeof(ExecutorExploreRoleNode), ServiceLifetime.Singleton)]
[Register(typeof(IAgentRoleNode), ServiceLifetime.Singleton)]
[NodeFactory(AgentRoleNodes.ConsumerId, Id)]
public sealed class ExecutorExploreRoleNode : IAgentRoleNode {
    /// <summary>节点 ID 常量。</summary>
    public const string Id = "executor-explore";

    /// <inheritdoc />
    public string NodeId => Id;

    /// <inheritdoc />
    public AgentRoleProfile Profile => AgentRoleNodes.ProfileOf(Id);
}

/// <summary>计划执行者角色节点。</summary>
[Register(typeof(ExecutorPlanRoleNode), ServiceLifetime.Singleton)]
[Register(typeof(IAgentRoleNode), ServiceLifetime.Singleton)]
[NodeFactory(AgentRoleNodes.ConsumerId, Id)]
public sealed class ExecutorPlanRoleNode : IAgentRoleNode {
    /// <summary>节点 ID 常量。</summary>
    public const string Id = "executor-plan";

    /// <inheritdoc />
    public string NodeId => Id;

    /// <inheritdoc />
    public AgentRoleProfile Profile => AgentRoleNodes.ProfileOf(Id);
}

/// <summary>医生执行者角色节点。</summary>
[Register(typeof(ExecutorDoctorRoleNode), ServiceLifetime.Singleton)]
[Register(typeof(IAgentRoleNode), ServiceLifetime.Singleton)]
[NodeFactory(AgentRoleNodes.ConsumerId, Id)]
public sealed class ExecutorDoctorRoleNode : IAgentRoleNode {
    /// <summary>节点 ID 常量。</summary>
    public const string Id = "executor-doctor";

    /// <inheritdoc />
    public string NodeId => Id;

    /// <inheritdoc />
    public AgentRoleProfile Profile => AgentRoleNodes.ProfileOf(Id);
}

/// <summary>验证执行者角色节点。</summary>
[Register(typeof(ExecutorVerificationRoleNode), ServiceLifetime.Singleton)]
[Register(typeof(IAgentRoleNode), ServiceLifetime.Singleton)]
[NodeFactory(AgentRoleNodes.ConsumerId, Id)]
public sealed class ExecutorVerificationRoleNode : IAgentRoleNode {
    /// <summary>节点 ID 常量。</summary>
    public const string Id = "executor-verification";

    /// <inheritdoc />
    public string NodeId => Id;

    /// <inheritdoc />
    public AgentRoleProfile Profile => AgentRoleNodes.ProfileOf(Id);
}

/// <summary>JoinCode 引导执行者角色节点。</summary>
[Register(typeof(ExecutorJoinCodeGuideRoleNode), ServiceLifetime.Singleton)]
[Register(typeof(IAgentRoleNode), ServiceLifetime.Singleton)]
[NodeFactory(AgentRoleNodes.ConsumerId, Id)]
public sealed class ExecutorJoinCodeGuideRoleNode : IAgentRoleNode {
    /// <summary>节点 ID 常量。</summary>
    public const string Id = "executor-join-code-guide";

    /// <inheritdoc />
    public string NodeId => Id;

    /// <inheritdoc />
    public AgentRoleProfile Profile => AgentRoleNodes.ProfileOf(Id);
}

/// <summary>上下文压缩执行者角色节点。</summary>
[Register(typeof(ExecutorContextCompressionRoleNode), ServiceLifetime.Singleton)]
[Register(typeof(IAgentRoleNode), ServiceLifetime.Singleton)]
[NodeFactory(AgentRoleNodes.ConsumerId, Id)]
public sealed class ExecutorContextCompressionRoleNode : IAgentRoleNode {
    /// <summary>节点 ID 常量。</summary>
    public const string Id = "executor-context-compression";

    /// <inheritdoc />
    public string NodeId => Id;

    /// <inheritdoc />
    public AgentRoleProfile Profile => AgentRoleNodes.ProfileOf(Id);
}
