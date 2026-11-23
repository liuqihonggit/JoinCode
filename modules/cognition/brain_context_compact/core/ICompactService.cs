
namespace Core.Context.Compact;

/// <summary>
/// 压缩服务接口 — 提供自动压缩、部分压缩和阈值判断能力
/// </summary>
public interface ICompactService {
    /// <summary>
    /// 执行自动压缩
    /// </summary>
    /// <param name="request">压缩请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>压缩结果</returns>
    Task<CompactResult> CompactAsync(CompactRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行部分压缩 — 根据枢轴索引和方向对消息子集生成摘要
    /// </summary>
    /// <param name="request">部分压缩请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>压缩结果</returns>
    Task<CompactResult> PartialCompactAsync(PartialCompactRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 判断是否应触发自动压缩
    /// </summary>
    /// <param name="currentTokenCount">当前 token 数</param>
    /// <param name="contextWindowTokens">上下文窗口大小</param>
    /// <returns>应触发返回 true，否则 false</returns>
    bool ShouldAutoCompact(int currentTokenCount, int contextWindowTokens);

    /// <summary>
    /// 判断是否应展示软压缩提示
    /// </summary>
    /// <param name="currentTokenCount">当前 token 数</param>
    /// <param name="contextWindowTokens">上下文窗口大小</param>
    /// <returns>应展示提示返回 true，否则 false</returns>
    bool ShouldSoftCompactNotice(int currentTokenCount, int contextWindowTokens);

    /// <summary>
    /// 计算压缩警告状态
    /// </summary>
    /// <param name="currentTokenCount">当前 token 数</param>
    /// <param name="contextWindowTokens">上下文窗口大小</param>
    /// <returns>压缩警告状态</returns>
    CompactWarningState CalculateWarningState(int currentTokenCount, int contextWindowTokens);
}

/// <summary>
/// 压缩请求
/// </summary>
public sealed class CompactRequest {
    /// <summary>待压缩的消息列表</summary>
    public required IReadOnlyList<ApiMessage> Messages { get; init; }
    /// <summary>压缩触发方式</summary>
    public CompactTrigger Trigger { get; init; } = CompactTrigger.Manual;
    /// <summary>自定义压缩指令</summary>
    public string? CustomInstructions { get; init; }
    /// <summary>是否抑制后续问题</summary>
    public bool SuppressFollowUpQuestions { get; init; }
    /// <summary>是否为自主模式</summary>
    public bool IsAutonomousMode { get; init; }
    /// <summary>转录文件路径</summary>
    public string? TranscriptPath { get; init; }
}

/// <summary>
/// 部分压缩请求
/// </summary>
public sealed class PartialCompactRequest {
    /// <summary>待压缩的消息列表</summary>
    public required IReadOnlyList<ApiMessage> Messages { get; init; }
    /// <summary>枢轴消息索引</summary>
    public required int PivotIndex { get; init; }
    /// <summary>压缩方向</summary>
    public CompactDirection Direction { get; init; } = CompactDirection.From;
    /// <summary>自定义压缩指令</summary>
    public string? CustomInstructions { get; init; }
    /// <summary>用户反馈</summary>
    public string? UserFeedback { get; init; }
}

/// <summary>
/// 压缩警告状态
/// </summary>
public sealed class CompactWarningState {
    /// <summary>剩余百分比</summary>
    public required int PercentLeft { get; init; }
    /// <summary>是否超过警告阈值</summary>
    public required bool IsAboveWarningThreshold { get; init; }
    /// <summary>是否超过错误阈值</summary>
    public required bool IsAboveErrorThreshold { get; init; }
    /// <summary>是否超过自动压缩阈值</summary>
    public required bool IsAboveAutoCompactThreshold { get; init; }
    /// <summary>是否达到阻塞上限</summary>
    public required bool IsAtBlockingLimit { get; init; }
    /// <summary>是否超过软压缩阈值</summary>
    public required bool IsAboveSoftCompactThreshold { get; init; }
}