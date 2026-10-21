namespace JoinCode.Gui.WebHost;

/// <summary>
/// 宿主事件日志（ADR 0143 M2-c3）：每个会话一份 <c>{journalDir}/{sessionId}.jsonl</c>（一行一条事件），
/// 记录宿主已经发给前端（或将要发给前端）的全部 DSH 事件。
/// <para>为什么需要：事件序号由宿主分配、引擎转录按条目重算，两者在「工具事件 / 未落盘轮次」上会分叉。
/// 前端会断言翻页结果恰好结束在请求的光标上（<c>page did not end at its requested cursor</c>），
/// 也会在重连时拒绝倒退的游标；因此宿主必须把它们发过的事件持久化，重开后按同一序号续写。</para>
/// </summary>
internal sealed class DshSessionJournal {
    private readonly IFileSystem _fs;
    private readonly string _directory;
    private readonly SemaphoreSlim _appendGate = new(1, 1);

    /// <summary>构造事件日志</summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="directory">日志目录（每会话一个 JSONL 文件）</param>
    internal DshSessionJournal(IFileSystem fs, string directory) {
        _fs = fs;
        _directory = directory;
    }

    /// <summary>读取会话的日志文件路径</summary>
    /// <param name="sessionId">会话 ID</param>
    internal string PathFor(string sessionId) => _fs.CombinePath(_directory, sessionId + ".jsonl");

    /// <summary>从磁盘载入会话的全部事件（按 seq 升序；文件不存在返回空数组）</summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    internal async Task<DshSessionEvent[]> LoadAsync(string sessionId, CancellationToken cancellationToken = default) {
        var path = PathFor(sessionId);
        if (!_fs.FileExists(path)) {
            return [];
        }
        var lines = await _fs.ReadAllLinesAsync(path, cancellationToken);
        var events = new List<DshSessionEvent>(lines.Length);
        foreach (var line in lines) {
            if (string.IsNullOrWhiteSpace(line)) {
                continue;
            }
            var sessionEvent = JsonSerializer.Deserialize(line, DshJsonContext.Default.DshSessionEvent);
            if (sessionEvent is not null) {
                events.Add(sessionEvent);
            }
        }
        events.Sort((left, right) => left.Seq.CompareTo(right.Seq));
        return [.. events];
    }

    /// <summary>追加一条事件并落盘（跨会话写入串行，保证文件不交错）</summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="sessionEvent">事件</param>
    /// <param name="cancellationToken">取消令牌</param>
    internal async Task AppendAsync(string sessionId, DshSessionEvent sessionEvent, CancellationToken cancellationToken = default) {
        var path = PathFor(sessionId);
        var directory = _fs.GetParentPath(path);
        if (directory is not null && !_fs.DirectoryExists(directory)) {
            _fs.CreateDirectory(directory);
        }
        var line = JsonSerializer.Serialize(sessionEvent, DshJsonContext.Default.DshSessionEvent) + "\n";
        await _appendGate.WaitAsync(cancellationToken);
        try {
            await _fs.AppendAllTextAsync(path, line, cancellationToken);
        } finally {
            _appendGate.Release();
        }
    }
}
