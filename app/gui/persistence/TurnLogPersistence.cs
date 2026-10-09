namespace JoinCode.Gui.Persistence;

/// <summary>
/// 轮次日志持久化 — 每轮结束后追加内容到 turns.log，撤回时重命名为 .undo 后缀（任务8）。
/// 遵循"用移动代替删除"原则（AGENTS.md）：撤回不删除，而是重命名保留审计追踪。
/// 文件位置：{sessionsDir}/{sessionId}/turns.log，撤回后变为 turns_{timestamp}.undo。
/// </summary>
public sealed class TurnLogPersistence {
    private readonly IFileSystem _fs;
    private readonly string _sessionsDir;

    /// <summary>构造轮次日志持久化</summary>
    /// <param name="fs">文件系统抽象（生产用 PhysicalFileSystem，测试用 InMemoryFileSystem）</param>
    /// <param name="sessionsDir">会话根目录</param>
    public TurnLogPersistence(IFileSystem fs, string sessionsDir) {
        _fs = fs;
        _sessionsDir = sessionsDir;
    }

    /// <summary>
    /// 追加一轮对话到 turns.log — 在每轮结束后调用。
    /// 格式：[TurnIndex] [Role] [Timestamp]\n内容\n---\n
    /// </summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="turnIndex">轮次编号</param>
    /// <param name="entries">本轮消息条目（User + Assistant + 工具等）</param>
    public async Task AppendTurnAsync(string sessionId, int turnIndex, IReadOnlyList<TurnLogEntry> entries) {
        if (entries.Count == 0)
            return;

        var logPath = GetLogPath(sessionId);
        var dir = Path.GetDirectoryName(logPath) ?? AppContext.BaseDirectory;
        if (!_fs.DirectoryExists(dir))
            _fs.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.AppendLine($"[Turn {turnIndex}] {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        foreach (var entry in entries) {
            sb.Append($"  [{entry.Role}] ").AppendLine(entry.Content);
        }
        sb.AppendLine("---");

        await _fs.AppendAllTextAsync(logPath, sb.ToString());
    }

    /// <summary>
    /// 标记撤回 — 将当前 turns.log 重命名为 turns_{timestamp}.undo，保留审计追踪。
    /// 下次 AppendTurnAsync 会创建新的 turns.log。无 turns.log 时空操作。
    /// </summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="turnIndex">被撤回的轮次编号（用于日志文件名）</param>
    public Task MarkUndoAsync(string sessionId, int turnIndex) {
        var logPath = GetLogPath(sessionId);
        if (!_fs.FileExists(logPath))
            return Task.CompletedTask;

        var undoPath = Path.Combine(
            Path.GetDirectoryName(logPath) ?? AppContext.BaseDirectory,
            $"turns_{turnIndex}_{DateTime.Now:yyyyMMdd_HHmmss}.undo");
        _fs.MoveFile(logPath, undoPath);
        return Task.CompletedTask;
    }

    /// <summary>获取当前轮次日志路径</summary>
    private string GetLogPath(string sessionId)
        => Path.Combine(_sessionsDir, sessionId, "turns.log");
}

/// <summary>轮次日志条目 — 角色与内容</summary>
public sealed record TurnLogEntry(string Role, string Content);
