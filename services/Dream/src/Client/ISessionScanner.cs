
namespace JoinCode.Dream.Services;

/// <summary>
/// 会话扫描器接口 - 扫描历史会话
/// </summary>
public interface ISessionScanner
{
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
[Register]
public sealed partial class DefaultSessionScanner : ISessionScanner
{
    private readonly string _projectDir;
    private readonly IFileSystem _fs;

    public DefaultSessionScanner(AutoDreamConfig config, IFileSystem fs)
    {
        _projectDir = config?.ProjectDir ?? fs.GetCurrentDirectory();
        _fs = fs;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListSessionsTouchedSinceAsync(
        long sinceMs,
        CancellationToken ct = default)
    {
        // 简化实现：扫描项目目录下的会话文件
        // 实际实现应该扫描特定的会话存储目录
        var sessions = new List<string>();

        try
        {
            var sessionsDir = Path.Combine(_projectDir, AppDataConstants.AppDataFolder, "sessions");
            if (!_fs.DirectoryExists(sessionsDir))
            {
                return Task.FromResult<IReadOnlyList<string>>(sessions);
            }

            var sinceTime = new DateTime(sinceMs, DateTimeKind.Utc);

            foreach (var file in _fs.EnumerateFiles(sessionsDir, "*.jsonl", SearchOption.TopDirectoryOnly))
            {
                var lastWriteTime = _fs.GetLastWriteTimeUtc(file);
                if (lastWriteTime > sinceTime)
                {
                    // 从文件名提取会话ID
                    var sessionId = Path.GetFileNameWithoutExtension(file);
                    if (!string.IsNullOrEmpty(sessionId))
                    {
                        sessions.Add(sessionId);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // 忽略扫描错误
            System.Diagnostics.Trace.WriteLine($"DefaultSessionScanner: Failed to scan sessions: {ex.Message}");
        }

        return Task.FromResult<IReadOnlyList<string>>(sessions);
    }

    /// <inheritdoc />
    public string GetProjectDir() => _projectDir;
}
