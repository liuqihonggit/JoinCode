namespace Core.Context;

/// <summary>
/// 任务进度追踪器 — 基于 ITodoService 追踪 TODO 表完成数，供循环检测判断任务是否真正推进
/// </summary>
[Register]
public sealed partial class TaskProgressTracker : ITaskProgressTracker
{
    [Inject] private readonly ITodoService _todoService;
    [Inject] private readonly ILogger<TaskProgressTracker>? _logger;
    private int _lastSnapshotCompletedCount;
    private bool _hasSnapshot;

    public async Task<int> GetCompletedTodoCountAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _todoService.ListTodosAsync(includeCompleted: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            return result.Success ? result.CompletedCount : 0;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[TaskProgressTracker] 获取TODO完成数失败");
            return 0;
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
