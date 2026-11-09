namespace Core.Context;

/// <summary>
/// 任务进度追踪器 — 基于 ITodoService 追踪 TODO 表完成数，供循环检测判断任务是否真正推进
/// </summary>
[Register(typeof(ITaskProgressTracker), ServiceLifetime.Singleton)]
public sealed partial class TaskProgressTracker : ServiceEntity, ITaskProgressTracker {

    /// <summary>
    /// 初始化任务进度追踪器
    /// </summary>
    /// <param name="todoService">TODO 服务</param>
    /// <param name="logger">可选日志记录器</param>
    public TaskProgressTracker(ITodoService todoService, ILogger<TaskProgressTracker>? logger = null) {
        _todoService = todoService;
        _logger = logger;
    }
    private readonly ITodoService _todoService;
    private readonly ILogger<TaskProgressTracker>? _logger;
    private int _lastSnapshotCompletedCount;
    private int _lastKnownCompletedCount;
    private bool _hasSnapshot;

    /// <summary>
    /// 获取当前已完成的 TODO 数量，查询失败时保留上次成功值
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>已完成的 TODO 数量</returns>
    public async Task<int> GetCompletedTodoCountAsync(CancellationToken cancellationToken = default) {
        try {
            var result = await _todoService.ListTodosAsync(includeCompleted: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (result.Success) {
                _lastKnownCompletedCount = result.CompletedCount;
                return result.CompletedCount;
            }

            _logger?.LogWarning("[TaskProgressTracker] TODO 查询失败(Success=false)，保留上次成功值 {Count}", _lastKnownCompletedCount);
            return _lastKnownCompletedCount;
        } catch (Exception ex) {
            // 查询失败不返回 0 — 否则基线被清零会误报"无推进"，触发误伤压缩。
            // 返回上次成功读取的计数，保持进度判定稳定。
            _logger?.LogWarning(ex, "[TaskProgressTracker] 获取TODO完成数失败，保留上次成功值 {Count}", _lastKnownCompletedCount);
            return _lastKnownCompletedCount;
        }
    }

    /// <summary>
    /// 快照当前 TODO 进度，作为后续进度判定的基线
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task SnapshotCurrentProgressAsync(CancellationToken cancellationToken = default) {
        _lastSnapshotCompletedCount = await GetCompletedTodoCountAsync(cancellationToken).ConfigureAwait(false);
        _hasSnapshot = true;
        _logger?.LogDebug("[TaskProgressTracker] 快照TODO进度：完成数={Count}", _lastSnapshotCompletedCount);
    }

    /// <summary>
    /// 判断自上次快照以来任务是否有推进（TODO 完成数是否增加）
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>有推进返回 true，无推进或未快照返回 false</returns>
    public async Task<bool> HasProgressedSinceLastSnapshotAsync(CancellationToken cancellationToken = default) {
        if (!_hasSnapshot)
            return false;

        var currentCount = await GetCompletedTodoCountAsync(cancellationToken).ConfigureAwait(false);
        var hasProgressed = currentCount > _lastSnapshotCompletedCount;

        if (hasProgressed) {
            _logger?.LogInformation("[TaskProgressTracker] 任务有推进：完成数从{Prev}变为{Curr}", _lastSnapshotCompletedCount, currentCount);
        }

        return hasProgressed;
    }
}