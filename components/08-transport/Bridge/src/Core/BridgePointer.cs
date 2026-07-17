
namespace Core.Bridge;

#region BridgePointerSource 枚举

/// <summary>
/// 桥指针来源 — 对齐 TS 端 bridgePointer.ts source 字段
/// </summary>
public enum BridgePointerSource
{
    /// <summary>独立模式</summary>
    [EnumValue("standalone")] Standalone,
    /// <summary>交互模式</summary>
    [EnumValue("REPL")] Repl,
}

#endregion

#region BridgePointer 数据模型 — 对齐 TS 端 bridgePointer.ts

/// <summary>
/// 崩溃恢复指针 — 对齐 TS 端 BridgePointer
/// 记录活跃会话信息，用于崩溃后恢复
/// </summary>
public sealed class BridgePointer
{
    [JsonPropertyName("sessionId")]
    public required string SessionId { get; init; }

    [JsonPropertyName("environmentId")]
    public required string EnvironmentId { get; init; }

    [JsonPropertyName("source")]
    public required string Source { get; init; } // BridgePointerSource.ToValue()
}

/// <summary>
/// 带年龄的指针 — 对齐 TS 端 readBridgePointer 返回值
/// </summary>
public sealed class BridgePointerWithAge
{
    public required BridgePointer Pointer { get; init; }
    public long AgeMs { get; init; }
}

#endregion

/// <summary>
/// 崩溃恢复指针服务 — 对齐 TS 端 bridgePointer.ts
/// 持久化活跃会话信息到文件，崩溃后可恢复
/// </summary>
public sealed class BridgePointerService
{
    private const int PointerTtlMs = 4 * 60 * 60 * 1000; // 4 小时
    private const int MaxWorktreeFanout = 50;
    private readonly ILogger? _logger;
    private readonly IFileSystem _fs;
    private readonly IClockService _clock;

    public BridgePointerService(IFileSystem fs, ILogger? logger = null, IClockService? clock = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
    }

    /// <summary>
    /// 获取指针文件路径 — 对齐 TS 端 getBridgePointerPath
    /// ~/.jcc/projects/{dir}/bridge-pointer.json
    /// </summary>
    public static string GetPointerPath(string dir)
    {
        ArgumentNullException.ThrowIfNull(dir);
        var appData = Environment.GetEnvironmentVariable("JCC_APP_DATA_FOLDER")
                   ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "jcc");
        var safeDir = dir.Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_');
        var projectsDir = Path.Combine(appData, "projects", safeDir);
        return Path.Combine(projectsDir, "bridge-pointer.json");
    }

    /// <summary>
    /// 写入指针 — 对齐 TS 端 writeBridgePointer
    /// 也用于刷新 mtime（保持指针新鲜度）
    /// </summary>
    public async Task WriteAsync(string dir, BridgePointer pointer, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dir);
        ArgumentNullException.ThrowIfNull(pointer);

        var path = GetPointerPath(dir);
        var directory = Path.GetDirectoryName(path)!;

        if (!_fs.DirectoryExists(directory))
        {
            _fs.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(pointer, BridgeJsonContext.Default.BridgePointer);
        await _fs.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);

        _logger?.LogDebug("[BridgePointer] 写入指针: {Path}, SessionId={SessionId}", path, pointer.SessionId);
    }

    /// <summary>
    /// 读取指针 — 对齐 TS 端 readBridgePointer
    /// 包含 mtime 新鲜度检查（4 小时 TTL）
    /// </summary>
    public async Task<BridgePointerWithAge?> ReadAsync(string dir, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dir);

        var path = GetPointerPath(dir);
        if (!_fs.FileExists(path))
        {
            return null;
        }

        try
        {
            var json = await _fs.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            var pointer = JsonSerializer.Deserialize(json, BridgeJsonContext.Default.BridgePointer);

            if (pointer is null) return null;

            var lastWrite = _fs.GetLastWriteTimeUtc(path);
            var ageMs = (long)(_clock.GetUtcNowOffset() - lastWrite).TotalMilliseconds;

            // 新鲜度检查 — 超过 4 小时视为过期
            if (ageMs > PointerTtlMs)
            {
                _logger?.LogDebug("[BridgePointer] 指针已过期: {AgeMs}ms > {TtlMs}ms", ageMs, PointerTtlMs);
                return null;
            }

            return new BridgePointerWithAge { Pointer = pointer, AgeMs = ageMs };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[BridgePointer] 读取指针失败: {Path}", path);
            return null;
        }
    }

    /// <summary>
    /// 跨 worktree 查找指针 — 对齐 TS 端 readBridgePointerAcrossWorktrees
    /// 并行搜索 worktree 目录，选择最新的有效指针
    /// </summary>
    public async Task<(BridgePointerWithAge Pointer, string Dir)?> ReadAcrossWorktreesAsync(
        string baseDir, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(baseDir);

        // 查找 worktree 目录
        var worktreeDirs = new List<string>();
        try
        {
            if (_fs.DirectoryExists(baseDir))
            {
                foreach (var dir in _fs.EnumerateDirectories(baseDir, "w*", SearchOption.TopDirectoryOnly))
                {
                    if (worktreeDirs.Count >= MaxWorktreeFanout) break;
                    worktreeDirs.Add(dir);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[BridgePointer] 搜索 worktree 目录失败: {BaseDir}", baseDir);
        }

        // 也搜索基础目录（尾部添加，避免 Insert(0) O(n)）
        worktreeDirs.Add(baseDir);

        // 并行读取所有指针
        var tasks = worktreeDirs.Select<string, Task<(BridgePointerWithAge Pointer, string Dir)?>>(async dir =>
        {
            var result = await ReadAsync(dir, ct).ConfigureAwait(false);
            return result is not null ? (Pointer: result, Dir: dir) : null;
        }).ToArray();

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        // 选择最新的（ageMs 最小的）
        (BridgePointerWithAge Pointer, string Dir)? best = null;
        foreach (var item in results)
        {
            if (item is null) continue;
            if (best is null || item.Value.Pointer.AgeMs < best.Value.Pointer.AgeMs)
            {
                best = item;
            }
        }

        return best;
    }

    /// <summary>
    /// 清除指针 — 对齐 TS 端 clearBridgePointer
    /// </summary>
    public Task ClearAsync(string dir, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dir);

        var path = GetPointerPath(dir);
        if (_fs.FileExists(path))
        {
            _fs.DeleteFile(path);
            _logger?.LogDebug("[BridgePointer] 清除指针: {Path}", path);
        }

        return Task.CompletedTask;
    }
}
