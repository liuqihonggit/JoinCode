namespace JoinCode.Dream.Services;

/// <summary>
/// 梦境整合锁 — .consolidate-lock 文件（TASK071 服务域-②③，对齐 TS consolidationLock.ts:14-36 +
/// autoDream.ts:255-273 的语义）：
/// 成功合并后 WriteNowAsync（跨重启持久）；kill/失败时 RollbackAsync(priorMtime) 回写旧值使时间门
/// 重新放行（kill 可安全重试）；ReadLastConsolidatedAtAsync 驱动时间门（旧实现 UtcNow-MinHours 假值 —
/// 刚合并完仍满足"距上次≥24h"的重复合并风险）。
/// 与 TS 的设计偏差：TS 用文件 mtime 承载 lastConsolidatedAt；C# IFileSystem 无文件级 mtime setter
/// （仅目录级），改用文件内容显式存毫秒时间戳 — 更稳健（备份/同步工具 touch 不破坏语义），无外部工具消费此文件的 mtime。
/// </summary>
public sealed class ConsolidationLock {
    private readonly string _lockPath;
    private readonly IFileSystem _fs;
    private readonly ILogger<ConsolidationLock>? _logger;

    /// <summary>锁文件名（对齐 TS .consolidate-lock）。</summary>
    public const string LockFileName = ".consolidate-lock";

    /// <summary>
    /// 构造整合锁 — 锁文件位于 ProjectDir（缺省 SessionsDirectory 根）下的 .consolidate-lock。
    /// </summary>
    /// <param name="config">自动做梦配置（ProjectDir 定位锁文件）。</param>
    /// <param name="fs">文件系统抽象。</param>
    /// <param name="logger">可选日志。</param>
    public ConsolidationLock(AutoDreamConfig config, IFileSystem fs, ILogger<ConsolidationLock>? logger = null) {
        var baseDir = config?.ProjectDir;
        _lockPath = Path.Combine(
            string.IsNullOrWhiteSpace(baseDir) ? AppDataConstants.Paths.SessionsDirectory : baseDir,
            LockFileName);
        _fs = fs;
        _logger = logger;
    }

    /// <summary>
    /// 读取上次合并完成时刻（锁文件内容的毫秒时间戳 UTC）— 从未合并/不可读返回 0（时间门天然放行）。
    /// </summary>
    public async Task<long> ReadLastConsolidatedAtAsync(CancellationToken ct = default) {
        try {
            if (!_fs.FileExists(_lockPath)) {
                return 0L;
            }

            var content = await _fs.ReadAllTextAsync(_lockPath, ct).ConfigureAwait(false);
            return long.TryParse(content.Trim(), out var ms) && ms > 0 ? ms : 0L;
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "[ConsolidationLock] 读取上次合并时间失败（按从未合并处理）");
            return 0L;
        }
    }

    /// <summary>
    /// 记录合并完成（当前时刻写入锁文件）— 合并成功收尾调用，跨重启持久。
    /// </summary>
    public async Task WriteNowAsync(CancellationToken ct = default) {
        var dir = Path.GetDirectoryName(_lockPath);
        if (!string.IsNullOrEmpty(dir)) {
            Infrastructure.Utils.IO.DirectoryHelper.EnsureDirectoryExists(_fs, dir);
        }

        await _fs.WriteAllTextAsync(_lockPath, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            .ToString(System.Globalization.CultureInfo.InvariantCulture), ct).ConfigureAwait(false);
        _logger?.LogDebug("[ConsolidationLock] 合并完成已记录");
    }

    /// <summary>
    /// 回滚锁到先前值 — kill/失败时调用（对齐 TS rollbackConsolidationLock），
    /// 时间门重新放行使 dream 可安全重试；priorMtime≤0 时删除锁文件（从未合并语义）。
    /// </summary>
    public async Task RollbackAsync(long priorMtime, CancellationToken ct = default) {
        try {
            if (priorMtime <= 0) {
                if (_fs.FileExists(_lockPath)) {
                    _fs.DeleteFile(_lockPath);
                }

                return;
            }

            var dir = Path.GetDirectoryName(_lockPath);
            if (!string.IsNullOrEmpty(dir)) {
                Infrastructure.Utils.IO.DirectoryHelper.EnsureDirectoryExists(_fs, dir);
            }

            await _fs.WriteAllTextAsync(_lockPath, priorMtime
                .ToString(System.Globalization.CultureInfo.InvariantCulture), ct).ConfigureAwait(false);
            _logger?.LogDebug("[ConsolidationLock] 锁已回滚至 {PriorMs}ms", priorMtime);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "[ConsolidationLock] 锁回滚失败（时间门可能提前放行）");
        }
    }
}
