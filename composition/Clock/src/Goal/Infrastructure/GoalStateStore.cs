namespace Core.Goal;

using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.Interfaces.Scheduling;
using JoinCode.Abstractions.Models.Goal;

/// <summary>
/// 目标状态持久化存储 — JSON 文件实现，按 sessionId 隔离，原子写入，AOT 兼容。
/// 路径: {baseDir}/{sessionId}/{goalId}.json
/// </summary>
[Register]
public sealed class GoalStateStore : IGoalStateStore
{
    private readonly string _baseDir;
    private readonly IFileSystem _fs;
    private readonly ILogger<GoalStateStore>? _logger = null;

    public GoalStateStore(IFileSystem fs, string? baseDir = null, ILogger<GoalStateStore>? logger = null)
    {
        _fs = fs;
        _baseDir = baseDir ?? _fs.CombinePath(AppContext.BaseDirectory, ".goal-state");
        _logger = logger;
    }

    /// <summary>
    /// 加载目标状态（不存在返回 null）
    /// </summary>
    public async Task<GoalState?> LoadAsync(string sessionId, string goalId, CancellationToken cancellationToken = default)
    {
        var path = GetPath(sessionId, goalId);
        if (!_fs.FileExists(path))
            return null;

        var json = await _fs.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize(json, GoalJsonContext.Default.GoalState);
    }

    /// <summary>
    /// 保存目标状态（原子写入：临时文件 + 重命名）。state.SessionId 确定隔离目录。
    /// </summary>
    public async Task SaveAsync(GoalState state, CancellationToken cancellationToken = default)
    {
        var sessionDir = GetSessionDir(state.SessionId);
        _fs.CreateDirectory(sessionDir);
        var path = _fs.CombinePath(sessionDir, $"{state.GoalId}.json");
        var json = JsonSerializer.Serialize(state, GoalJsonContext.Default.GoalState);

        var tempPath = path + ".tmp";
        await _fs.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
        _fs.MoveFile(tempPath, path, overwrite: true);

        _logger?.LogDebug("[GoalStateStore] 保存目标状态: {GoalId} (会话: {SessionId}, 状态: {Status})", state.GoalId, state.SessionId, state.Status);
    }

    /// <summary>
    /// 删除目标状态
    /// </summary>
    public Task DeleteAsync(string sessionId, string goalId, CancellationToken cancellationToken = default)
    {
        var path = GetPath(sessionId, goalId);
        if (_fs.FileExists(path))
            _fs.DeleteFile(path);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取指定会话的所有未完成目标（Status=Pursuing 或 Paused）
    /// </summary>
    public async Task<IReadOnlyList<GoalState>> GetActiveGoalsAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var sessionDir = GetSessionDir(sessionId);
        if (!_fs.DirectoryExists(sessionDir))
            return [];

        var result = new List<GoalState>();
        foreach (var file in _fs.EnumerateFiles(sessionDir, "*.json", SearchOption.TopDirectoryOnly))
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

    private string GetSessionDir(string sessionId) => _fs.CombinePath(_baseDir, sessionId);
    private string GetPath(string sessionId, string goalId) => _fs.CombinePath(GetSessionDir(sessionId), $"{goalId}.json");
}
