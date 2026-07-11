namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 编译队列服务 — 编译请求串行化 + 结果缓冲区
/// </summary>
public interface IBuildQueueService : IAsyncDisposable
{
    /// <summary>
    /// 提交编译请求 → 立即返回 buildId，不阻塞 LLM
    /// </summary>
    Task<string> SubmitAsync(BuildRequest request, CancellationToken ct);

    /// <summary>
    /// 主动等待编译完成 → 阻塞直到指定 buildId 的编译完成并返回结果
    /// </summary>
    Task<BuildQueueResult> WaitAsync(string buildId, CancellationToken ct);

    /// <summary>
    /// 取消编译 — 正在编译则杀进程，排队中则移出队列
    /// </summary>
    Task<bool> CancelAsync(string buildId, CancellationToken ct);

    /// <summary>
    /// 查询编译状态（不阻塞）
    /// </summary>
    BuildQueueEntry? GetBuild(string buildId);

    /// <summary>
    /// 查询队列状态（诊断用）
    /// </summary>
    BuildQueueStatus GetStatus();

    /// <summary>
    /// 清除全部编译结果缓冲区
    /// </summary>
    Task ClearCacheAsync(CancellationToken ct);

    /// <summary>
    /// 获取编译输出的指定行范围 — AI 渐进式阅读编译结果
    /// </summary>
    /// <param name="buildId">编译 ID</param>
    /// <param name="startLine">起始行号（1-based）</param>
    /// <param name="endLine">结束行号（含，0=到末尾）</param>
    string GetOutputRange(string buildId, int startLine, int endLine = 0);
}
