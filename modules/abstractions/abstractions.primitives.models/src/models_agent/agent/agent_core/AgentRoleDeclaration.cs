namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 角色启用声明 — profile 消费点 <c>agent-roles</c> 的声明结果，由装配层构造并注入。
/// <para>
/// TASK100 S3：<c>role</c> 契约的语义是「同 role+variant 互斥」，声明只表达**启用集合**。
/// 禁用某个角色不等于报错：注册表不登记它，查询按既有语义返回 null（调用方本就处理了 null），
/// 这与 <c>provider</c> 契约不同 —— 协议缺失会导致请求发错格式，角色缺失只是少一个可选能力。
/// </para>
/// <para>
/// 未注入（如不装配 Composition 的宿主）时视为全部启用，保持历史行为。
/// </para>
/// </summary>
public sealed class AgentRoleDeclaration {
    /// <summary>角色在 profile 中的消费点 ID（contract: role）。</summary>
    public const string ConsumerId = "agent-roles";

    private readonly HashSet<string> _enabled;
    private readonly bool _restricted;

    private AgentRoleDeclaration(HashSet<string> enabled, bool restricted) {
        _enabled = enabled;
        _restricted = restricted;
    }

    /// <summary>未声明（全部启用）的实例。</summary>
    public static AgentRoleDeclaration AllEnabled { get; } = new([], restricted: false);

    /// <summary>
    /// 从 profile 的节点 ID 列表构造声明。
    /// </summary>
    /// <param name="declaredNodeIds">profile 消费点 <c>agent-roles</c> 声明的节点 ID；null 或空表示不限制。</param>
    /// <returns>启用声明</returns>
    public static AgentRoleDeclaration FromDeclaredIds(IReadOnlyList<string>? declaredNodeIds) {
        if (declaredNodeIds is null || declaredNodeIds.Count == 0)
            return AllEnabled;

        var enabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var nodeId in declaredNodeIds) {
            if (!string.IsNullOrWhiteSpace(nodeId))
                enabled.Add(nodeId);
        }

        return new AgentRoleDeclaration(enabled, restricted: true);
    }

    /// <summary>判断某角色节点是否已启用。</summary>
    /// <param name="nodeId">角色节点 ID（如 executor-code / coordinator）。</param>
    /// <returns>启用返回 true</returns>
    public bool IsEnabled(string nodeId) => !_restricted || _enabled.Contains(nodeId);
}
