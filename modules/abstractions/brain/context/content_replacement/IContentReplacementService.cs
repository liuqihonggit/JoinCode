// JCC11005 抑制: 存量返回 null 语义, 后续评估改 TryGet
#pragma warning disable JCC11005
namespace JoinCode.Abstractions.Brain.Context;

/// <summary>
/// 内容替换服务接口，对齐 TS 工具结果预算与持久化机制，
/// 在超长工具结果上做磁盘持久化并返回占位替换字符串
/// </summary>
public interface IContentReplacementService {
    /// <summary>
    /// 对齐 TS maybePersistLargeToolResult — 纯函数，不修改任何 state
    /// 仅检查内容大小和工具阈值，超限时持久化到磁盘并返回替换字符串
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="toolUseId">工具调用 ID</param>
    /// <param name="content">工具结果内容</param>
    /// <param name="sessionId">会话 ID</param>
    /// <returns>替换字符串；未超限或持久化失败时返回 null</returns>
    ValueTask<string?> MaybePersistLargeToolResult(string toolName, string toolUseId, string content, string sessionId);

    /// <summary>
    /// 对齐 TS applyToolResultBudget — 返回处理后的消息和新产生的替换记录
    /// newlyReplaced: 本次调用新做出的替换决策（不含 mustReapply 的重新应用）
    /// ⚠️ 副作用: 会原地修改 state 参数 — 向 state.SeenIds 添加已见 ID，向 state.Replacements 添加新替换映射
    /// 需要隔离时先调用 state.Clone() 克隆副本（对齐 TS cloneContentReplacementState）
    /// 异步: 对齐 TS Promise.all 并发持久化，串行文件 I/O → 并行
    /// </summary>
    /// <param name="messages">原始消息列表</param>
    /// <param name="state">内容替换状态，原地修改</param>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="neverPersistTools">永不持久化的工具名集合（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>处理后的消息列表与本次新产生的替换记录</returns>
    Task<(IReadOnlyList<ApiMessage> Messages, IReadOnlyList<ContentReplacementRecord> NewlyReplaced)> ApplyToolResultBudgetAsync(
        IReadOnlyList<ApiMessage> messages,
        ContentReplacementState state,
        string sessionId,
        HashSet<string>? neverPersistTools = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 从替换记录重建内容替换状态
    /// </summary>
    /// <param name="records">替换记录列表</param>
    /// <returns>重建后的状态</returns>
    ContentReplacementState ReconstructState(IReadOnlyList<ContentReplacementRecord> records);

    /// <summary>
    /// 对齐 TS reconstructContentReplacementState — 从消息历史+记录+继承替换重建状态
    /// </summary>
    /// <param name="messages">消息历史</param>
    /// <param name="records">替换记录列表</param>
    /// <param name="inheritedReplacements">继承的替换映射（可选）</param>
    /// <returns>重建后的状态</returns>
    ContentReplacementState ReconstructState(
        IReadOnlyList<ApiMessage> messages,
        IReadOnlyList<ContentReplacementRecord> records,
        IReadOnlyDictionary<string, string>? inheritedReplacements = null);

    /// <summary>
    /// 对齐 TS provisionContentReplacementState — 功能开关入口
    /// 功能开关关闭时返回 null（query 会跳过整个预算执行）
    /// 有初始消息时走重建路径，冷启动时走新建路径
    /// </summary>
    /// <param name="initialMessages">初始消息列表（可选）</param>
    /// <param name="initialContentReplacements">初始替换记录（可选）</param>
    /// <returns>功能开关关闭返回 null；否则返回新建或重建的状态</returns>
    ContentReplacementState? ProvisionContentReplacementState(
        IReadOnlyList<ApiMessage>? initialMessages = null,
        IReadOnlyList<ContentReplacementRecord>? initialContentReplacements = null);

    /// <summary>
    /// 对齐 TS reconstructForSubagentResume — 子智能体恢复重建
    /// 传入父级 state 的 replacements 作为继承替换源
    /// 父级状态为 null 时直接返回 null（功能开关关闭）
    /// </summary>
    /// <param name="parentState">父级内容替换状态（可选）</param>
    /// <param name="resumedMessages">恢复后的消息列表</param>
    /// <param name="sidechainRecords">子链替换记录</param>
    /// <returns>父级状态为 null 时返回 null；否则返回重建后的状态</returns>
    ContentReplacementState? ReconstructForSubagentResume(
        ContentReplacementState? parentState,
        IReadOnlyList<ApiMessage> resumedMessages,
        IReadOnlyList<ContentReplacementRecord> sidechainRecords);
}
