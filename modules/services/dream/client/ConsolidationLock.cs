namespace JoinCode.Dream.Services;

/// <summary>
/// 梦境整合锁 — .consolidate-lock 文件（TASK071 服务域-②③，对齐 TS consolidationLock.ts:14-36 +
/// autoDream.ts:255-273 的语义）：
/// 成功合并后 WriteNowAsync（跨重启持久）；kill/失败时 RollbackAsync(priorMtime) 回写旧值使时间门
/// 重新放行（kill 可安全重试）；ReadLastConsolidatedAtAsync 驱动时间门（旧实现 UtcNow-MinHours 假值 —
/// 刚合并完仍满足"距上次≥24h"的重复合并风险）。
/// 与 TS 的设计偏差：TS 用文件 mtime 承载 lastConsolidatedAt；C# IFileSystem 无文件级 mtime setter
/// （仅目录级），改用文件内容显式存毫秒时间戳 — 更稳健（备份/同步工具 touch 不破坏语义），无外部工具消费此文件的 mtime。
/// <para>
/// TASK126（#40）追加**原子占锁**：文件内容改为 <c>{pid}:{tsMs}</c>，对齐 TS
/// <c>tryAcquireConsolidationLock</c> 的「pid 存活 + 超时回收 + 写后复核」三步。
/// 没有它时「时间门」防不住并发 —— 一次整合要跑几分钟，期间每个回合读到的
/// lastConsolidatedAt 都还是旧值，于是每个回合都会再发起一份 LLM 调用。
/// 读取侧同时兼容 TASK071 的旧格式（纯毫秒数字，无 pid）。
/// </para>
/// </summary>
public sealed class ConsolidationLock {
    private readonly string _lockPath;
    private readonly IFileSystem _fs;
    private readonly ILogger<ConsolidationLock>? _logger;

    /// <summary>锁文件名（对齐 TS .consolidate-lock）。</summary>
    public const string LockFileName = ".consolidate-lock";

    /// <summary>持锁超过此时长即视为过期（即使 pid 还活着）— 对齐 TS HOLDER_STALE_MS，防 pid 复用。</summary>
    public const long HolderStaleMs = 60 * 60 * 1000;

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

    /// <summary>锁文件绝对路径（测试与诊断用）。</summary>
    public string LockPath => _lockPath;

    /// <summary>
    /// 读取上次合并完成时刻（锁文件内容的毫秒时间戳 UTC）— 从未合并/不可读返回 0（时间门天然放行）。
    /// </summary>
    public async Task<long> ReadLastConsolidatedAtAsync(CancellationToken ct = default) {
        var (_, tsMs) = await ReadBodyAsync(ct).ConfigureAwait(false);
        return tsMs;
    }

    /// <summary>
    /// 原子占锁 — 成功返回**占锁前的**上次合并时刻（失败时用它回滚），失败返回 null（别人在跑/抢输了）。
    /// <para>三步对齐 TS <c>tryAcquireConsolidationLock</c>：①持锁未过期且 pid 活着 → 让出；
    /// ②否则写入自己的 pid + 当前时刻；③写后复读，pid 不是自己说明并发对手后写赢了 → 让出。</para>
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <param name="isProcessAlive">pid 存活判定（缺省用系统进程表；测试注入桩）。</param>
    public async Task<long?> TryAcquireAsync(CancellationToken ct = default, Func<int, bool>? isProcessAlive = null) {
        isProcessAlive ??= DefaultIsProcessAlive;

        var (holderPid, holderTs) = await ReadBodyAsync(ct).ConfigureAwait(false);
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (holderTs > 0) {
            var heldMs = nowMs - holderTs;
            if (heldMs < HolderStaleMs && holderPid > 0 && isProcessAlive(holderPid)) {
                _logger?.LogDebug("[ConsolidationLock] 锁被活进程 {Pid} 持有（{HeldSeconds}s 前），本次让出", holderPid, heldMs / 1000);
                return null;
            }

            // 旧格式（pid≤0）不当作持有者：那是 TASK071 的「完成时刻」语义，没有持有者。
            // 抢写后由下面的复核步定胜负，不需要在这里让出。
            _logger?.LogDebug("[ConsolidationLock] 回收过期/无主锁（pid={Pid}，{HeldSeconds}s）", holderPid, heldMs / 1000);
        }

        await WriteBodyAsync(Environment.ProcessId, nowMs, ct).ConfigureAwait(false);

        // 两个抢占者都写 → 后写者赢 pid；输的那个复读发现不是自己，让出。
        var (verifyPid, _) = await ReadBodyAsync(ct).ConfigureAwait(false);
        if (verifyPid != Environment.ProcessId) {
            _logger?.LogDebug("[ConsolidationLock] 占锁复核失败（当前 pid={Pid}），本次让出", verifyPid);
            return null;
        }

        return holderTs;
    }

    /// <summary>
    /// 记录合并完成（当前时刻写入锁文件）— 合并成功收尾调用，跨重启持久。
    /// </summary>
    public Task WriteNowAsync(CancellationToken ct = default) =>
        WriteBodyAsync(Environment.ProcessId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ct);

    /// <summary>
    /// 回滚锁到先前值 — kill/失败时调用（对齐 TS rollbackConsolidationLock），
    /// 时间门重新放行使 dream 可安全重试；priorMtime≤0 时删除锁文件（从未合并语义）。
    /// <para>回滚后体内**不留自己的 pid**（写裸时间戳），否则本进程看起来仍持有锁。</para>
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

    private async Task<(int Pid, long TsMs)> ReadBodyAsync(CancellationToken ct) {
        try {
            if (!_fs.FileExists(_lockPath)) return (0, 0L);
            var content = await _fs.ReadAllTextAsync(_lockPath, ct).ConfigureAwait(false);
            return ParseBody(content);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "[ConsolidationLock] 读取锁文件失败（按从未合并处理）");
            return (0, 0L);
        }
    }

    private async Task WriteBodyAsync(int pid, long tsMs, CancellationToken ct) {
        var dir = Path.GetDirectoryName(_lockPath);
        if (!string.IsNullOrEmpty(dir)) {
            Infrastructure.Utils.IO.DirectoryHelper.EnsureDirectoryExists(_fs, dir);
        }

        var body = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0}:{1}",
            pid,
            tsMs);
        await _fs.WriteAllTextAsync(_lockPath, body, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 解析锁文件内容 — <c>{pid}:{tsMs}</c>；旧格式（纯毫秒数字）按 pid=0 处理。
    /// </summary>
    internal static (int Pid, long TsMs) ParseBody(string? content) {
        var text = content?.Trim();
        if (string.IsNullOrEmpty(text)) return (0, 0L);

        var separator = text.IndexOf(':');
        if (separator <= 0) {
            return (0, long.TryParse(text, out var legacy) && legacy > 0 ? legacy : 0L);
        }

        var pid = int.TryParse(text.AsSpan(0, separator), out var parsedPid) ? parsedPid : 0;
        var ts = long.TryParse(text.AsSpan(separator + 1), out var parsedTs) && parsedTs > 0 ? parsedTs : 0L;
        return (pid, ts);
    }

    private static bool DefaultIsProcessAlive(int pid) {
        if (pid <= 0) return false;
        try {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        } catch (ArgumentException) {
            return false; // 进程不存在
        } catch (InvalidOperationException) {
            return false;
        }
    }
}
