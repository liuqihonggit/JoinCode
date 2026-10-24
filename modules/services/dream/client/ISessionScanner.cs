
namespace JoinCode.Dream.Services;

/// <summary>
/// 会话扫描器接口 - 扫描历史会话
/// </summary>
public interface ISessionScanner {
    /// <summary>
    /// 列出指定时间之后被修改的会话
    /// </summary>
    Task<IReadOnlyList<string>> ListSessionsTouchedSinceAsync(
        long sinceMs,
        CancellationToken ct = default);

    /// <summary>
    /// 获取项目目录
    /// </summary>
    string GetProjectDir();
}

/// <summary>
/// 默认会话扫描器实现
/// </summary>
[Register(typeof(ISessionScanner), ServiceLifetime.Singleton)]
public sealed partial class DefaultSessionScanner : ServiceEntity, ISessionScanner {
    private readonly string _projectDir;
    private readonly IFileSystem _fs;
    private readonly ILogger<DefaultSessionScanner>? _logger;

    /// <summary>
    /// 构造默认会话扫描器
    /// </summary>
    /// <param name="config">自动做梦配置</param>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="logger">日志记录器</param>
    public DefaultSessionScanner(AutoDreamConfig config, IFileSystem fs, ILogger<DefaultSessionScanner>? logger = null) {
        _projectDir = config?.ProjectDir ?? fs.GetCurrentDirectory();
        _fs = fs;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListSessionsTouchedSinceAsync(
        long sinceMs,
        CancellationToken ct = default) {
        // TASK071 服务域-① — 会话布局为 SessionsDirectory/<sessionId>/transcript.jsonl
        // （TranscriptService.ResolveTranscriptFile 同源）；旧实现扫顶层 *.json 恒 0 会话（autoDream 门控失效）
        var sessions = new List<string>();

        try {
            var sessionsDir = AppDataConstants.Paths.SessionsDirectory;
            if (!_fs.DirectoryExists(sessionsDir)) {
                return Task.FromResult<IReadOnlyList<string>>(sessions);
            }

            var sinceTime = new DateTime(sinceMs * TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);

            foreach (var dir in _fs.EnumerateDirectories(sessionsDir, "*", SearchOption.TopDirectoryOnly)) {
                ct.ThrowIfCancellationRequested();
                var sessionId = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(sessionId)) continue;

                // transcript.jsonl 优先（TASK057 JSONL 化），回退存量 transcript.json
                var transcriptPath = Path.Combine(dir, "transcript.jsonl");
                if (!_fs.FileExists(transcriptPath)) {
                    transcriptPath = Path.Combine(dir, "transcript.json");
                    if (!_fs.FileExists(transcriptPath)) continue;
                }

                var lastWriteTime = _fs.GetLastWriteTimeUtc(transcriptPath);
                if (lastWriteTime > sinceTime) {
                    sessions.Add(sessionId);
                }
            }
        } catch (Exception ex) {
            // 忽略扫描错误
            _logger?.LogWarning(ex, "DefaultSessionScanner: 扫描会话失败");
        }

        return Task.FromResult<IReadOnlyList<string>>(sessions);
    }

    /// <inheritdoc />
    public string GetProjectDir() => _projectDir;
}