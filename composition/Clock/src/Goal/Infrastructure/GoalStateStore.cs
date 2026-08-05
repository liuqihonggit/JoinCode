namespace Core.Goal;

using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.Interfaces.Scheduling;
using JoinCode.Abstractions.Models.Goal;

/// <summary>
/// 目标状态持久化存储 — JSON 文件实现，原子写入，AOT 兼容。
/// 路径: {baseDir}/{goalId}.json
/// </summary>
[Register]
public sealed class GoalStateStore : IGoalStateStore
{
    private readonly string _baseDir;
    private readonly IFileSystem _fs;
    [Inject] private readonly ILogger<GoalStateStore>? _logger = null;

    public GoalStateStore(IFileSystem fs, string? baseDir = null, ILogger<GoalStateStore>? logger = null)
    {
        _fs = fs;
        _baseDir = baseDir ?? _fs.CombinePath(AppContext.BaseDirectory, ".goal-state");
        _logger = logger;
    }

    /// <summary>
    /// 加载目标状态（不存在返回 null）
    /// </summary>
    public async Task<GoalState?> LoadAsync(string goalId, CancellationToken cancellationToken = default)
    {
        var path = GetPath(goalId);
        if (!_fs.FileExists(path))
            return null;

        var json = await _fs.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize(json, GoalJsonContext.Default.GoalState);
    }

    /// <summary>
    /// 保存目标状态（原子写入：临时文件 + 重命名）
    /// </summary>
    public async Task SaveAsync(GoalState state, CancellationToken cancellationToken = default)
    {
        _fs.CreateDirectory(_baseDir);
        var path = GetPath(state.GoalId);
        var json = JsonSerializer.Serialize(state, GoalJsonContext.Default.GoalState);

        var tempPath = path + ".tmp";
        await _fs.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
        _fs.MoveFile(tempPath, path, overwrite: true);

        _logger?.LogDebug("[GoalStateStore] 保存目标状态: {GoalId} ({Status})", state.GoalId, state.Status);
    }

    /// <summary>
    /// 删除目标状态
    /// </summary>
    public Task DeleteAsync(string goalId, CancellationToken cancellationToken = default)
    {
        var path = GetPath(goalId);
        if (_fs.FileExists(path))
            _fs.DeleteFile(path);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取所有未完成的目标（Status=Pursuing 或 Paused）
    /// </summary>
    public async Task<IReadOnlyList<GoalState>> GetActiveGoalsAsync(CancellationToken cancellationToken = default)
    {
        if (!_fs.DirectoryExists(_baseDir))
            return [];

        var result = new List<GoalState>();
        foreach (var file in _fs.EnumerateFiles(_baseDir, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var json = await _fs.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
                var state = JsonSerializer.Deserialize(json, GoalJsonContext.Default.GoalState);
                if (state is not null && (state.Status == GoalStatus.Pursuing || state.Status == GoalStatus.Paused))
                    result.Add(state);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[GoalStateStore] 读取文件失败: {File}", file);
            }
        }
        return result;
    }

    private string GetPath(string goalId) => _fs.CombinePath(_baseDir, $"{goalId}.json");
}
