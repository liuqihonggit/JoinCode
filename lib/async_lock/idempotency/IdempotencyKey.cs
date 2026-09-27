namespace Core.Utils;

/// <summary>
/// 幂等键 — 业务流水号 + 操作标识的组合，用于键控去重。
/// <para><b>业务流水号</b>代表一次业务意图，重试不变；<b>操作标识</b>区分同一业务意图下的不同操作。</para>
/// <para>与 RequestId 区别：RequestId 是会话标识，单次重试可更换；幂等键在重试中保持不变。</para>
/// </summary>
/// <param name="BusinessFlowId">业务流水号 — 代表一次业务意图，重试不变</param>
/// <param name="OperationId">操作标识 — 区分同一业务意图下的不同操作</param>
public readonly record struct IdempotencyKey(string BusinessFlowId, string OperationId) {
    /// <summary>空键 — 表示无幂等控制</summary>
    public static readonly IdempotencyKey None = default;

    /// <summary>是否为空键（无幂等控制）</summary>
    public bool IsEmpty => BusinessFlowId is null && OperationId is null;
}
