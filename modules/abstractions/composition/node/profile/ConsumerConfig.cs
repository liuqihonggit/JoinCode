namespace JoinCode.Abstractions.Composition.Profile;

/// <summary>
/// 消费点配置 — 一个消费点包含一组有序的节点
/// </summary>
/// <param name="ConsumerId">消费点 ID（如 chat-pipeline）。</param>
/// <param name="Nodes">该消费点下的节点条目。</param>
/// <param name="Contract">
/// 装配契约（见 <see cref="NodeContract"/>）。<see langword="null"/> 表示 YAML 未声明，
/// 由继承链决定：解析时缺省为 <see cref="NodeContract.Middleware"/>，子 Profile 省略即继承父级。
/// </param>
public sealed record ConsumerConfig(
    string ConsumerId,
    IReadOnlyList<NodeEntryConfig> Nodes,
    string? Contract = null
);
