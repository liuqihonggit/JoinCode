namespace Core.Agents.Coordinator;

/// <summary>
/// TeamManager 持久化方法 — partial class，分离文件 IO 逻辑以控制文件长度。
/// 序列化到 ~/.jcc/teams/state.json，支持 CLI 无状态模式跨进程共享团队状态。
/// </summary>
public sealed partial class TeamManager
{
    private readonly IFileSystem? _persistenceFs;
    private readonly string? _stateFilePath;

    /// <summary>
    /// 获取团队状态文件路径: ~/.jcc/teams/state.json
    /// </summary>
    private static string GetStateFilePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            AppDataConstants.AppDataFolder,
            AppDataConstants.TeamsFolderName,
            "state.json");
    }

    /// <summary>
    /// 从磁盘加载团队状态。文件不存在或读取失败时静默跳过（不影响启动）。
    /// </summary>
    private void LoadState()
    {
        if (_persistenceFs is null || _stateFilePath is null) return;

        try
        {
            if (!_persistenceFs.FileExists(_stateFilePath)) return;

            var json = _persistenceFs.ReadAllText(_stateFilePath);
            if (string.IsNullOrWhiteSpace(json)) return;

            var data = RelaxedJsonSerializer.Deserialize(json, TeamPersistenceJsonContext.Default.TeamStateData);
            if (data is null) return;

            // 恢复团队
            if (data.Teams is not null)
            {
                foreach (var team in data.Teams)
                {
                    _teams[team.TeamId] = team;
                }
            }

            // 恢复团队成员
            if (data.TeamMembers is not null)
            {
                foreach (var kvp in data.TeamMembers)
                {
                    _teamMembers[kvp.Key] = new HashSet<string>(kvp.Value);
                }
            }

            // 恢复团队消息
            if (data.TeamMessages is not null)
            {
                foreach (var kvp in data.TeamMessages)
                {
                    _teamMessages[kvp.Key] = kvp.Value;
                }
            }

            // 恢复团队成员详情
            if (data.TeamMemberDetails is not null)
            {
                foreach (var kvp in data.TeamMemberDetails)
                {
                    _teamMemberDetails[kvp.Key] = kvp.Value.ToDictionary(m => m.AgentId, m => m);
                }
            }

            // 恢复代理到团队映射
            if (data.AgentToTeam is not null)
            {
                foreach (var kvp in data.AgentToTeam)
                {
                    _agentToTeam[kvp.Key] = kvp.Value;
                }
            }

            // 恢复计数器（取较大值避免 ID 冲突）
            _teamCounter = Math.Max(_teamCounter, data.TeamCounter);
            _messageCounter = Math.Max(_messageCounter, data.MessageCounter);

            _logger?.LogDebug("团队状态已从 {FilePath} 加载: {TeamCount} 个团队", _stateFilePath, _teams.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "加载团队状态失败，将使用空状态启动");
        }
    }

    /// <summary>
    /// 保存团队状态到磁盘。写入失败时静默跳过（不影响操作结果）。
    /// </summary>
    private async Task SaveStateAsync(CancellationToken cancellationToken = default)
    {
        if (_persistenceFs is null || _stateFilePath is null) return;

        try
        {
            var data = new TeamStateData
            {
                Teams = _teams.Values.ToList(),
                TeamMembers = _teamMembers.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToList()),
                TeamMessages = _teamMessages.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToList()),
                TeamMemberDetails = _teamMemberDetails.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Values.ToList()),
                AgentToTeam = new Dictionary<string, string>(_agentToTeam),
                TeamCounter = _teamCounter,
                MessageCounter = _messageCounter
            };

            var dir = Path.GetDirectoryName(_stateFilePath);
            if (dir is not null && !_persistenceFs.DirectoryExists(dir))
            {
                _persistenceFs.CreateDirectory(dir);
            }

            var json = RelaxedJsonSerializer.Serialize(data, TeamPersistenceJsonContext.Default);
            await _persistenceFs.WriteAllTextAsync(_stateFilePath, json, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "保存团队状态失败");
        }
    }
}
