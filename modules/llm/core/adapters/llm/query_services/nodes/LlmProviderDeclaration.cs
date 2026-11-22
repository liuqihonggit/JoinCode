namespace Api.LLM.QueryServices.Nodes;

/// <summary>
/// 协议启用声明 — profile 消费点 <c>llm-providers</c> 的声明结果，由装配层构造并注入。
/// <para>
/// TASK100 S3：<c>provider</c> 契约的语义是「同协议内互斥」，因此 profile 的顺序对它没有意义，
/// 声明只表达**启用集合**：不在集合内的协议直接拒绝，而不是静默退回默认实现
/// （静默退回会把「Anthropic 配置被当成 OpenAI 格式发出去」这种事故藏起来）。
/// </para>
/// <para>
/// 未注入（如 Dream 组件这类不装配 Composition 的宿主）时视为全部启用，保持历史行为。
/// </para>
/// </summary>
public sealed class LlmProviderDeclaration {
    private readonly HashSet<ProtocolKind> _enabled;
    private readonly bool _restricted;

    private LlmProviderDeclaration(HashSet<ProtocolKind> enabled, bool restricted) {
        _enabled = enabled;
        _restricted = restricted;
    }

    /// <summary>未声明（全部启用）的实例。</summary>
    public static LlmProviderDeclaration AllEnabled { get; } = new([], restricted: false);

    /// <summary>
    /// 从 profile 的节点 ID 列表构造声明。
    /// </summary>
    /// <param name="declaredNodeIds">profile 消费点 <c>llm-providers</c> 声明的节点 ID；null 或空表示不限制。</param>
    /// <returns>启用声明</returns>
    /// <exception cref="InvalidOperationException">声明了不存在的节点 ID（fail-closed，避免配置写错却静默生效）。</exception>
    public static LlmProviderDeclaration FromDeclaredIds(IReadOnlyList<string>? declaredNodeIds) {
        if (declaredNodeIds is null || declaredNodeIds.Count == 0)
            return AllEnabled;

        var enabled = new HashSet<ProtocolKind>();
        var unknown = new List<string>();
        foreach (var nodeId in declaredNodeIds) {
            var kind = LlmProviderNodes.KindOf(nodeId);
            if (kind is null)
                unknown.Add(nodeId);
            else
                enabled.Add(kind.Value);
        }

        if (unknown.Count > 0) {
            throw new InvalidOperationException(
                $"[LLM-PROVIDER-UNKNOWN] profile 消费点 '{LlmProviderNodes.ConsumerId}' 声明了不存在的节点: {string.Join(", ", unknown)}。" +
                $"可用节点: {string.Join(", ", LlmProviderNodes.AllNodeIds)}。");
        }

        return new LlmProviderDeclaration(enabled, restricted: true);
    }

    /// <summary>判断某协议是否已启用。</summary>
    /// <param name="kind">协议类型</param>
    /// <returns>启用返回 true</returns>
    public bool IsEnabled(ProtocolKind kind) => !_restricted || _enabled.Contains(kind);

    /// <summary>断言协议已启用，否则抛出带处置建议的错误。</summary>
    /// <param name="kind">协议类型</param>
    /// <exception cref="InvalidOperationException">协议未在 profile 中声明。</exception>
    public void AssertEnabled(ProtocolKind kind) {
        if (!_restricted || _enabled.Contains(kind))
            return;

        throw new InvalidOperationException(
            $"[LLM-PROVIDER-DISABLED] 协议 '{kind}' 未在 profile 消费点 '{LlmProviderNodes.ConsumerId}' 中声明，已按配置拒绝。" +
            $"当前已启用: {string.Join(", ", _enabled.Select(static k => k.ToString()).Order(StringComparer.Ordinal))}。" +
            $"处置: 在 resources/profiles/base.yaml 的 {LlmProviderNodes.ConsumerId} 下补节点声明，或换用已启用协议的供应商配置。");
    }
}
