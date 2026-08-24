namespace Core.Context;

/// <summary>
/// 任务进度追踪器 — 基于 ITodoService 追踪 TODO 表完成数，供循环检测判断任务是否真正推进
/// </summary>
[Register]
public sealed partial class TaskProgressTracker : ServiceEntity, ITaskProgressTracker
{

    public TaskProgressTracker(ITodoService todoService, ILogger<TaskProgressTracker>? logger = null)
    {
        _todoService = todoService;
        _logger = logger;
    }
    private readonly ITodoService _todoService;
    private readonly ILogger<TaskProgressTracker>? _logger;
    private int _lastSnapshotCompletedCount;
    private int _lastKnownCompletedCount;
    private bool _hasSnapshot;

    public async Task<int> GetCompletedTodoCountAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _todoService.ListTodosAsync(includeCompleted: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (result.Success)
            {
                _lastKnownCompletedCount = result.CompletedCount;
                return result.CompletedCount;
            }

            _logger?.LogWarning("[TaskProgressTracker] TODO 查询失败(Success=false)，保留上次成功值 {Count}", _lastKnownCompletedCount);
            return _lastKnownCompletedCount;
        }
        catch (Exception ex)
        {
            // 查询失败不返回 0 — 否则基线被清零会误报"无推进"，触发误伤压缩。
            // 返回上次成功读取的计数，保持进度判定稳定。
            _logger?.LogWarning(ex, "[TaskProgressTracker] 获取TODO完成数失败，保留上次成功值 {Count}", _lastKnownCompletedCount);
            return _lastKnownCompletedCount;
        }
    }

    public async Task SnapshotCurrentProgressAsync(CancellationToken cancellationToken = default)
    {
        _lastSnapshotCompletedCount = await GetCompletedTodoCountAsync(cancellationToken).ConfigureAwait(false);
        _hasSnapshot = true;
        _logger?.LogDebug("[TaskProgressTracker] 快照TODO进度：完成数={Count}", _lastSnapshotCompletedCount);
    }

    public async Task<bool> HasProgressedSinceLastSnapshotAsync(CancellationToken cancellationToken = default)
    {
        if (!_hasSnapshot)
            return false;

        var currentCount = await GetCompletedTodoCountAsync(cancellationToken).ConfigureAwait(false);
        var hasProgressed = currentCount > _lastSnapshotCompletedCount;

        if (hasProgressed)
        {
            _logger?.LogInformation("[TaskProgressTracker] 任务有推进：完成数从{Prev}变为{Curr}", _lastSnapshotCompletedCount, currentCount);
        }

        return hasProgressed;
    }
}
