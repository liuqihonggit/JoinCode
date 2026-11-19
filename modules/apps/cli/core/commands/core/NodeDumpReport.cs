namespace JoinCode.Cli;

/// <summary>
/// `jcc --dump-nodes --json` 的诊断报告 DTO —— 供一致性门禁脚本消费。
/// <para>TASK100：节点表与工厂 catalog 是两套事实，必须由同一份结构化输出做三方比对，
/// 脚本不再自行解析 C# 源码或人肉核对。</para>
/// </summary>
/// <param name="NodeCount">NodeRegistry 从编译期工厂 catalog 归纳的节点数。</param>
/// <param name="ConsumerCount">消费点总数（跨 profile 去重前）。</param>
/// <param name="Consumers">每个 profile 的消费点与节点明细。</param>
/// <param name="Verification">一致性校验结果。</param>
internal sealed record NodeDumpReportDto(
    int NodeCount,
    int ConsumerCount,
    NodeDumpConsumerDto[] Consumers,
    NodeDumpVerificationDto Verification
);

/// <summary>单个消费点的 dump 明细。</summary>
/// <param name="Profile">所属 profile 名。</param>
/// <param name="ConsumerId">消费点 ID。</param>
/// <param name="Contract">装配契约。</param>
/// <param name="Nodes">节点条目。</param>
internal sealed record NodeDumpConsumerDto(
    string Profile,
    string ConsumerId,
    string Contract,
    NodeDumpEntryDto[] Nodes
);

/// <summary>单个节点条目。</summary>
/// <param name="Id">节点 ID。</param>
/// <param name="Priority">装配位次。</param>
/// <param name="EnabledWhen">特性开关条件，未声明为 null。</param>
/// <param name="Config">节点配置键值。</param>
internal sealed record NodeDumpEntryDto(
    string Id,
    int Priority,
    string? EnabledWhen,
    IReadOnlyDictionary<string, string> Config
);

/// <summary>一致性校验结果 —— 三个数组均应为空。</summary>
/// <param name="DeclaredWithoutFactory">profile 声明了、工厂 catalog 里没有的节点（死声明）。</param>
/// <param name="FactoryWithoutDeclaration">工厂存在、但所有 profile 都不声明的节点（孤儿工厂）。</param>
/// <param name="DuplicateInExclusiveConsumer">互斥契约消费点内重复声明的节点 ID。</param>
internal sealed record NodeDumpVerificationDto(
    string[] DeclaredWithoutFactory,
    string[] FactoryWithoutDeclaration,
    string[] DuplicateInExclusiveConsumer
) {
    /// <summary>是否一致（三个数组全空）。</summary>
    public bool IsConsistent =>
        DeclaredWithoutFactory.Length == 0
        && FactoryWithoutDeclaration.Length == 0
        && DuplicateInExclusiveConsumer.Length == 0;
}

/// <summary>节点诊断报告的 AOT 源生成上下文。</summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(NodeDumpReportDto))]
internal sealed partial class NodeDumpJsonContext : JsonSerializerContext;
