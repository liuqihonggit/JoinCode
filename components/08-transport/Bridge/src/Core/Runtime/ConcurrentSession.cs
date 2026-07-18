
namespace Core.Bridge;

#region SessionKind 枚举

/// <summary>
/// 会话类型 — 对齐 TS 端 SessionKind
/// </summary>
public enum SessionKind
{
    /// <summary>交互式会话</summary>
    [EnumValue("interactive")] Interactive,
    /// <summary>后台会话</summary>
    [EnumValue("bg")] Background,
    /// <summary>守护进程会话</summary>
    [EnumValue("daemon")] Daemon,
    /// <summary>守护进程工作会话</summary>
    [EnumValue("daemon-worker")] DaemonWorker,
}

#endregion

#region SessionStatus 枚举

/// <summary>
/// 会话活动状态 — 对齐 TS 端 SessionStatus
/// </summary>
public enum SessionStatus
{
    /// <summary>忙碌</summary>
    [EnumValue("busy")] Busy,
    /// <summary>空闲</summary>
    [EnumValue("idle")] Idle,
    /// <summary>等待中</summary>
    [EnumValue("waiting")] Waiting,
}

#endregion

#region ConcurrentSessionRecord 数据模型 — 对齐 TS 端 concurrentSessions.ts PID 文件

/// <summary>
/// 并发会话记录 — 对齐 TS 端 PID 文件格式
/// 写入 {sessionsDir}/{pid}.json
/// </summary>
public sealed class ConcurrentSessionRecord
{
    [JsonPropertyName("pid")]
    public int Pid { get; init; }

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; init; }

    [JsonPropertyName("startedAt")]
    public long StartedAt { get; init; }

    [JsonPropertyName("kind")]
    public string? Kind { get; init; }

    [JsonPropertyName("entrypoint")]
    public string? Entrypoint { get; init; }

    /// <summary>桥会话兼容 ID — 对齐 TS 端 bridgeSessionId</summary>
    [JsonPropertyName("bridgeSessionId")]
    public string? BridgeSessionId { get; set; }

    /// <summary>会话名称 — 对齐 TS 端 name</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>活动状态 — 对齐 TS 端 status</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>等待内容 — 对齐 TS 端 waitingFor</summary>
    [JsonPropertyName("waitingFor")]
    public string? WaitingFor { get; set; }

    /// <summary>最后更新时间 — 对齐 TS 端 updatedAt</summary>
    [JsonPropertyName("updatedAt")]
    public long? UpdatedAt { get; set; }
}

#endregion

/// <summary>
/// 并发会话服务 — 对齐 TS 端 concurrentSessions.ts
/// 管理 PID 文件注册系统，记录活跃会话信息
/// </summary>
public sealed class ConcurrentSessionService
{
    private readonly IFileSystem _fs;
    private readonly ILogger? _logger;
    private readonly string _sessionsDir;
    private readonly IClockService _clock;

    public ConcurrentSessionService(IFileSystem fs, ILogger? logger = null, IClockService? clock = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _sessionsDir = GetSessionsDir();
    }

    /// <summary>
    /// 获取会话目录路径 — 对齐 TS 端 getSessionsDir()
    /// ~/.jcc/sessions/
    /// </summary>
    public static string GetSessionsDir()
    {
        var appData = Environment.GetEnvironmentVariable("JCC_APP_DATA_FOLDER")
                   ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppDataConstants.AppDataFolder);
        return Path.Combine(appData, "sessions");
    }

    /// <summary>
    /// 获取当前进程的 PID 文件路径
    /// </summary>
    public string GetPidFilePath() => Path.Combine(_sessionsDir, $"{Environment.ProcessId}.json");

    /// <summary>
    /// 注册当前会话 — 对齐 TS 端 registerSession()
    /// </summary>
    public async Task<bool> RegisterAsync(string? sessionId = null, CancellationToken ct = default)
    {
        var kind = Environment.GetEnvironmentVariable("JCC_SESSION_KIND") switch
        {
            "bg" => SessionKind.Background.ToValue(),
            "daemon" => SessionKind.Daemon.ToValue(),
            "daemon-worker" => SessionKind.DaemonWorker.ToValue(),
            _ => SessionKind.Interactive.ToValue(),
        };

        var record = new ConcurrentSessionRecord
        {
            Pid = Environment.ProcessId,
            SessionId = sessionId,
            Cwd = _fs.GetCurrentDirectory(),
            StartedAt = _clock.GetUtcNowOffset().ToUnixTimeMilliseconds(),
            Kind = kind,
            Entrypoint = Environment.GetEnvironmentVariable("JCC_ENTRYPOINT"),
        };

        try
        {
            if (!_fs.DirectoryExists(_sessionsDir))
            {
                _fs.CreateDirectory(_sessionsDir);
            }

            var json = JsonSerializer.Serialize(record, BridgeJsonContext.Default.ConcurrentSessionRecord);
            await _fs.WriteAllTextAsync(GetPidFilePath(), json, ct).ConfigureAwait(false);
            _logger?.LogDebug("[ConcurrentSession] 注册会话: PID={Pid}, SessionId={SessionId}", record.Pid, record.SessionId);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[ConcurrentSession] 注册会话失败");
            return false;
        }
    }

    /// <summary>
    /// 注销当前会话 — 对齐 TS 端 cleanupRegistry 中的 unlink
    /// </summary>
    public Task UnregisterAsync(CancellationToken ct = default)
    {
        var path = GetPidFilePath();
        if (_fs.FileExists(path))
        {
            try
            {
                _fs.DeleteFile(path);
                _logger?.LogDebug("[ConcurrentSession] 注销会话: PID={Pid}", Environment.ProcessId);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[ConcurrentSession] 注销会话失败");
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 更新 PID 文件 — 对齐 TS 端 updatePidFile(patch)
    /// 读取现有文件，应用更新，重写
    /// </summary>
    public async Task UpdateAsync(Action<ConcurrentSessionRecord> applyPatch, CancellationToken ct = default)
    {
        var path = GetPidFilePath();
        try
        {
            if (!_fs.FileExists(path))
            {
                _logger?.LogDebug("[ConcurrentSession] PID 文件不存在，跳过更新");
                return;
            }

            var json = await _fs.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            var existing = JsonSerializer.Deserialize(json, BridgeJsonContext.Default.ConcurrentSessionRecord);
            if (existing is null) return;

            applyPatch(existing);
            existing.UpdatedAt = _clock.GetUtcNowOffset().ToUnixTimeMilliseconds();

            var updatedJson = JsonSerializer.Serialize(existing, BridgeJsonContext.Default.ConcurrentSessionRecord);
            await _fs.WriteAllTextAsync(path, updatedJson, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[ConcurrentSession] 更新 PID 文件失败");
        }
    }

    /// <summary>
    /// 更新桥会话 ID — 对齐 TS 端 updateSessionBridgeId
    /// 记录 bridge session ID 以便 peer 去重
    /// </summary>
    public Task UpdateBridgeSessionIdAsync(string? bridgeSessionId, CancellationToken ct = default)
    {
        return UpdateAsync(r => r.BridgeSessionId = bridgeSessionId, ct);
    }

    /// <summary>
    /// 更新会话名称 — 对齐 TS 端 updateSessionName
    /// </summary>
    public Task UpdateSessionNameAsync(string name, CancellationToken ct = default)
    {
        return UpdateAsync(r => r.Name = name, ct);
    }

    /// <summary>
    /// 更新会话活动状态 — 对齐 TS 端 updateSessionActivity
    /// </summary>
    public Task UpdateSessionActivityAsync(string status, string? waitingFor = null, CancellationToken ct = default)
    {
        return UpdateAsync(r =>
        {
            r.Status = status;
            if (waitingFor is not null) r.WaitingFor = waitingFor;
        }, ct);
    }

    /// <summary>
    /// 统计并发会话数 — 对齐 TS 端 countConcurrentSessions
    /// 扫描 PID 文件，清理过期文件
    /// </summary>
    public int CountConcurrentSessions()
    {
        try
        {
            if (!_fs.DirectoryExists(_sessionsDir)) return 0;

            var count = 0;
            foreach (var file in _fs.EnumerateFiles(_sessionsDir, "*.json", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                if (!int.TryParse(fileName, out var pid)) continue;

                // 当前进程总是计数
                if (pid == Environment.ProcessId)
                {
                    count++;
                    continue;
                }

                // 检查进程是否仍在运行
                try
                {
                    var proc = System.Diagnostics.Process.GetProcessById(pid);
                    if (!proc.HasExited)
                    {
                        count++;
                    }
                    else
                    {
                        // 过期文件，清理
                        TryDeleteFile(file);
                    }
                }
                catch (ArgumentException)
                {
                    // 进程不存在，清理过期文件
                    TryDeleteFile(file);
                }
                catch (Exception)
                {
                    // 无法检测（如 WSL），保守计数
                    count++;
                }
            }

            return count;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[ConcurrentSession] 统计并发会话失败");
            return 0;
        }
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            _fs.DeleteFile(path);
            _logger?.LogDebug("[ConcurrentSession] 清理过期 PID 文件: {Path}", path);
        }
        catch (Exception ex) { /* best-effort */ System.Diagnostics.Trace.WriteLine($"[ConcurrentSession] Failed to delete PID file '{path}': {ex.Message}"); }
    }
}
