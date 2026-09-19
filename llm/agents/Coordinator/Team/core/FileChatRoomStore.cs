namespace Core.Agents.Coordinator;

/// <summary>
/// 文件系统聊天室存储 — 按需加载/保存单个聊天室到 ~/.jcc/teams/rooms/{teamId}.json — ADR 0109 决策13。
/// <para>对标 QQ 云端存档：本地只缓存活跃房间，历史房间按需从存储加载。</para>
/// </summary>
[Register(typeof(IChatRoomStore), ServiceLifetime.Singleton)]
public sealed class FileChatRoomStore : IChatRoomStore {
    private readonly IFileSystem _fs;
    private readonly string _roomsDir;
    private readonly ILogger<FileChatRoomStore>? _logger;

    /// <summary>
    /// 初始化文件聊天室存储
    /// </summary>
    /// <param name="fs">文件系统</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="roomsDirOverride">覆盖房间目录路径（测试用）</param>
    public FileChatRoomStore(IFileSystem fs, ILogger<FileChatRoomStore>? logger = null, string? roomsDirOverride = null) {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _logger = logger;
        _roomsDir = roomsDirOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            AppDataConstants.AppDataFolder,
            AppDataConstants.TeamsFolderName,
            "rooms");
    }

    /// <summary>获取聊天室文件路径</summary>
    private string GetRoomFilePath(string teamId) => Path.Combine(_roomsDir, $"{teamId}.json");

    /// <inheritdoc/>
    public async Task<ChatRoomState?> LoadAsync(string teamId, CancellationToken ct = default) {
        var filePath = GetRoomFilePath(teamId);
        if (!_fs.FileExists(filePath)) return null;

        try {
            var json = await _fs.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json)) return null;

            var data = RelaxedJsonSerializer.Deserialize(json, TeamPersistenceJsonContext.Default.ChatRoomStateData);
            return data?.ToState();
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger?.LogWarning(ex, "加载聊天室 {TeamId} 失败", teamId);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task SaveAsync(string teamId, ChatRoomState state, CancellationToken ct = default) {
        try {
            if (!_fs.DirectoryExists(_roomsDir)) {
                _fs.CreateDirectory(_roomsDir);
            }

            var filePath = GetRoomFilePath(teamId);
            var data = ChatRoomStateData.FromState(state);
            var json = RelaxedJsonSerializer.Serialize(data, TeamPersistenceJsonContext.Default);
            await _fs.WriteAllTextAsync(filePath, json, ct).ConfigureAwait(false);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger?.LogWarning(ex, "保存聊天室 {TeamId} 失败", teamId);
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> ListRoomIdsAsync(CancellationToken ct = default) {
        if (!_fs.DirectoryExists(_roomsDir)) {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        var files = _fs.GetFiles(_roomsDir, "*.json", SearchOption.TopDirectoryOnly);
        var ids = files
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Where(name => !string.IsNullOrEmpty(name))
            .ToList()!;

        return Task.FromResult<IReadOnlyList<string>>(ids);
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string teamId, CancellationToken ct = default) {
        var filePath = GetRoomFilePath(teamId);
        if (_fs.FileExists(filePath)) {
            _fs.DeleteFile(filePath);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<(string TeamId, int MessageCount, int MaxCount)>> GetRoomsNeedingCleanupAsync(CancellationToken ct = default) {
        var roomIds = await ListRoomIdsAsync(ct).ConfigureAwait(false);
        var result = new List<(string, int, int)>();

        foreach (var teamId in roomIds) {
            var state = await LoadAsync(teamId, ct).ConfigureAwait(false);
            if (state is not null && state.NeedsCleanup) {
                result.Add((teamId, state.MessageCount, state.MaxMessageCount));
            }
        }

        return result;
    }
}