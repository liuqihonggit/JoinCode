namespace AsyncFileLock;

/// <summary>
/// 跨进程租约 — 原子文件租约（<see cref="FileMode.CreateNew"/> 原子创建 + owner token + TTL + 心跳续期）。
/// <para>获取：CreateNew 成功即持有；失败读取现有 owner 的最后续期时间，未过期则等待，过期则原子改名归档后重试创建。</para>
/// <para>续期：持有期间通过打开的写句柄追加 renew 行（同时刷新文件时间戳），进程崩溃即停止续期。</para>
/// <para>接管：最后续期时间超过 TTL + 时钟容差后，任何进程可原子改名归档旧 owner 文件并重新创建。</para>
/// <para>释放：关闭句柄，校验 owner token 后删除租约文件；释放路径禁止等待（ADR 0093）。</para>
/// <para>日志：获取/续期/接管/释放均记录 owner token 与时间线，供多进程验收审计。</para>
/// </summary>
internal sealed class CrossProcessLock : IDisposable {
    /// <summary>租约文件后缀 — 租约文件位于 key 同级的 <c>{key}.jcclease</c>。</summary>
    internal const string LeaseSuffix = ".jcclease";

    /// <summary>默认 TTL — 最后一次续期后超过该时长视为 owner 已崩溃。</summary>
    internal static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(15);

    /// <summary>心跳间隔 — TTL 的 1/3，容忍两次连续丢失。</summary>
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);

    /// <summary>时钟容差 — 跨进程文件时间戳与本地时钟的允许偏差。</summary>
    private static readonly TimeSpan ClockSkewTolerance = TimeSpan.FromSeconds(2);

    /// <summary>租约文件超过该大小时心跳触发压缩重写。</summary>
    private const int MaxLeaseFileBytes = 32 * 1024;

    private readonly IFileSystem _fs;
    private readonly string _leasePath;
    private readonly Stream _ownerStream;
    private readonly ILogger? _logger;
    private readonly PeriodicTimer _heartbeatTimer;
    private volatile bool _stopping;
    private int _disposed;

    /// <summary>本次租约的 owner token — 诊断/审计用。</summary>
    public string Token { get; }

    /// <summary>租约文件绝对路径。</summary>
    public string LeasePath => _leasePath;

    /// <summary>owner 进程 ID。</summary>
    public int OwnerProcessId { get; }

    /// <summary>获取时间（UTC）。</summary>
    public DateTime AcquiredAtUtc { get; }

    private CrossProcessLock(IFileSystem fs, string leasePath, Stream ownerStream, string token, DateTime acquiredAtUtc, ILogger? logger) {
        _fs = fs;
        _leasePath = leasePath;
        _ownerStream = ownerStream;
        _logger = logger;
        _heartbeatTimer = new PeriodicTimer(HeartbeatInterval);
        Token = token;
        OwnerProcessId = Environment.ProcessId;
        AcquiredAtUtc = acquiredAtUtc;
    }

    /// <summary>
    /// 异步获取跨进程租约，超时未获取则抛出 <see cref="TimeoutException"/>。
    /// </summary>
    /// <param name="key">业务 key（文件路径或数据目录）— 租约文件为 <c>{key}.jcclease</c></param>
    /// <param name="timeout">等待上限；<see cref="TimeSpan.Zero"/> 表示只做一次非阻塞尝试（过期租约仍会立即接管）</param>
    /// <param name="ct">取消令牌</param>
    /// <param name="logger">可选日志器（记录 token/获取/续期/接管时间线）</param>
    /// <param name="fs">可选文件系统（默认 PhysicalFileSystem，经 FileSystemFactory）</param>
    public static async Task<CrossProcessLock> AcquireAsync(
        string key,
        TimeSpan timeout,
        CancellationToken ct,
        ILogger? logger = null,
        IFileSystem? fs = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var fileSystem = fs ?? FileSystemFactory.Create();
        var leasePath = ResolveLeasePath(fileSystem, key);
        var deadline = timeout == Timeout.InfiniteTimeSpan ? DateTime.MaxValue : DateTime.UtcNow + timeout;
        var pollMs = 20;
        var missingRetries = 0;

        while (true) {
            ct.ThrowIfCancellationRequested();

            var created = TryCreate(fileSystem, leasePath, logger);
            if (created is not null) {
                created.StartHeartbeat();
                return created;
            }

            var snapshot = LeaseSnapshot.Inspect(fileSystem, leasePath);
            if (!snapshot.Exists) {
                // TryCreate 与 Inspect 之间租约被释放/接管 — 重试创建；连续失败说明创建本身有问题，避免忙等
                if (++missingRetries > 3) {
                    throw new TimeoutException($"未能获取跨进程租约 '{key}'：租约文件 {leasePath} 无法创建（目录权限/路径无效），已重试 {missingRetries} 次");
                }
                continue;
            }
            missingRetries = 0;

            if (snapshot.IsExpired(DateTime.UtcNow)) {
                TryTakeOver(fileSystem, leasePath, snapshot, logger);
                continue;
            }

            if (timeout <= TimeSpan.Zero || DateTime.UtcNow >= deadline) {
                throw new TimeoutException(
                    $"未能获取跨进程租约 '{key}'（{timeout.TotalSeconds}s）：租约文件 {leasePath} 由 pid={snapshot.OwnerProcessId} token={snapshot.Token} 持有，" +
                    $"最后续期 {snapshot.LastRenewedUtc:O}；如需接管请等待 TTL {DefaultTtl.TotalSeconds}s");
            }

            await Task.Delay(pollMs, ct).ConfigureAwait(false);
            pollMs = Math.Min(pollMs * 2, 200);
        }
    }

    /// <summary>
    /// 计算 key 对应的租约文件路径 — 纯字符串规则（不探测目录是否存在，避免两侧判定分叉）。
    /// </summary>
    public static string ResolveLeasePath(IFileSystem fs, string key)
        => fs.GetFullPath(key) + LeaseSuffix;

    private static CrossProcessLock? TryCreate(IFileSystem fs, string leasePath, ILogger? logger) {
        var directory = Path.GetDirectoryName(leasePath);
        try {
            if (!string.IsNullOrEmpty(directory) && !fs.DirectoryExists(directory)) {
                fs.CreateDirectory(directory);
            }

            var stream = fs.CreateStream(leasePath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            try {
                var token = Guid.NewGuid().ToString("N");
                var acquiredAtUtc = DateTime.UtcNow;
                WriteLine(stream, BuildHeaderLine(token, acquiredAtUtc));
                logger?.LogInformation(
                    "跨进程租约获取: {LeasePath} token={Token} pid={Pid} at={AcquiredAt:O}",
                    leasePath, token, Environment.ProcessId, acquiredAtUtc);
                Diag.WriteLine($"[LEASE] acquire path={leasePath} token={token} pid={Environment.ProcessId} at={acquiredAtUtc:O}");
                return new CrossProcessLock(fs, leasePath, stream, token, acquiredAtUtc, logger);
            } catch {
                stream.DisposeSafe(logger);
                TryDeleteCreatedLease(fs, leasePath, logger);
                throw;
            }
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return null;
        }
    }

    private static void TryDeleteCreatedLease(IFileSystem fs, string leasePath, ILogger? logger) {
        try {
            fs.DeleteFile(leasePath);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            logger?.LogDebug(ex, "创建失败后清理租约文件失败（TTL 到期后可被接管）: {LeasePath}", leasePath);
        }
    }

    private static void TryTakeOver(IFileSystem fs, string leasePath, LeaseSnapshot snapshot, ILogger? logger) {
        var archivePath = $"{leasePath}.{DateTime.UtcNow:yyyyMMddHHmmssfff}.{Environment.ProcessId}.{Guid.NewGuid():N}.takeover";
        try {
            fs.MoveFile(leasePath, archivePath, overwrite: false);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return;
        }

        logger?.LogWarning(
            "跨进程租约接管: {LeasePath} 旧 token={Token} pid={Pid} 最后续期 {LastRenewed:O}，已归档到 {Archive}",
            leasePath, snapshot.Token, snapshot.OwnerProcessId, snapshot.LastRenewedUtc, archivePath);
        Diag.WriteLine($"[LEASE] takeover path={leasePath} oldToken={snapshot.Token} oldPid={snapshot.OwnerProcessId} lastRenewed={snapshot.LastRenewedUtc:O}");

        try {
            fs.DeleteFile(archivePath);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            logger?.LogWarning(ex, "跨进程租约归档文件删除失败（可手工清理）: {Archive}", archivePath);
        }
    }

    private void StartHeartbeat() => _ = Task.Run(HeartbeatLoopAsync);

    private async Task HeartbeatLoopAsync() {
        try {
            while (!_stopping && await _heartbeatTimer.WaitForNextTickAsync(CancellationToken.None).ConfigureAwait(false)) {
                if (_stopping) break;
                Renew();
            }
        } catch (Exception ex) when (ex is IOException or ObjectDisposedException or UnauthorizedAccessException) {
            _logger?.LogWarning(ex, "跨进程租约心跳停止: {LeasePath} token={Token}", _leasePath, Token);
        }
    }

    private void Renew() {
        if (_stopping) return;

        WriteLine(_ownerStream, $"renew|{DateTime.UtcNow.Ticks}\n");
        _logger?.LogDebug("跨进程租约续期: {LeasePath} token={Token}", _leasePath, Token);
        Diag.WriteLine($"[LEASE] renew path={_leasePath} token={Token} at={DateTime.UtcNow:O}");

        if (_ownerStream.CanSeek && _ownerStream.Length > MaxLeaseFileBytes) {
            Compact();
        }
    }

    private void Compact() {
        _ownerStream.Seek(0, SeekOrigin.Begin);
        WriteLine(_ownerStream, BuildHeaderLine(Token, DateTime.UtcNow));
        WriteLine(_ownerStream, $"renew|{DateTime.UtcNow.Ticks}\n");
        _ownerStream.SetLength(_ownerStream.Position);
        _ownerStream.Flush();
    }

    private static string BuildHeaderLine(string token, DateTime acquiredAtUtc)
        => $"lease|1|{token}|{Environment.ProcessId}|{(long)DefaultTtl.TotalMilliseconds}|{acquiredAtUtc.Ticks}\n";

    private static void WriteLine(Stream stream, string line) {
        var bytes = Encoding.UTF8.GetBytes(line);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }

    /// <summary>释放租约 — 停止心跳、关闭句柄、校验 token 后删除租约文件。幂等，禁止等待。</summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stopping = true;
        _heartbeatTimer.Dispose();
        _ownerStream.DisposeSafe(_logger);
        ReleaseLeaseFile();
    }

    private void ReleaseLeaseFile() {
        try {
            if (!_fs.FileExists(_leasePath)) return;

            string text;
            using (var stream = _fs.OpenRead(_leasePath))
            using (var reader = new StreamReader(stream)) {
                text = reader.ReadToEnd();
            }

            if (!text.Contains(Token, StringComparison.Ordinal)) {
                _logger?.LogWarning("跨进程租约已被接管，跳过删除: {LeasePath} token={Token}", _leasePath, Token);
                Diag.WriteLine($"[LEASE] release-skip path={_leasePath} token={Token} (已被接管)");
                return;
            }

            _fs.DeleteFile(_leasePath);
            _logger?.LogInformation("跨进程租约释放: {LeasePath} token={Token} at={ReleasedAt:O}", _leasePath, Token, DateTime.UtcNow);
            Diag.WriteLine($"[LEASE] release path={_leasePath} token={Token} at={DateTime.UtcNow:O}");
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            _logger?.LogWarning(ex, "跨进程租约文件删除失败（TTL 到期后可被接管）: {LeasePath}", _leasePath);
        }
    }

    /// <summary>租约文件快照 — 解析 owner token/pid/最后续期时间，判定是否过期。</summary>
    private sealed class LeaseSnapshot {
        private readonly string _reason;

        private LeaseSnapshot(string? token, int ownerProcessId, DateTime lastRenewedUtc, TimeSpan ttl, bool exists, string reason) {
            Token = token;
            OwnerProcessId = ownerProcessId;
            LastRenewedUtc = lastRenewedUtc;
            Ttl = ttl;
            Exists = exists;
            _reason = reason;
        }

        /// <summary>owner token（无完整记录时为 null）。</summary>
        public string? Token { get; }

        /// <summary>owner 进程 ID（未知为 0）。</summary>
        public int OwnerProcessId { get; }

        /// <summary>最后续期时间（UTC）。</summary>
        public DateTime LastRenewedUtc { get; }

        /// <summary>owner 声明的 TTL。</summary>
        public TimeSpan Ttl { get; }

        /// <summary>租约文件是否存在。</summary>
        public bool Exists { get; }

        /// <summary>租约文件不存在。</summary>
        public static LeaseSnapshot Missing => new(null, 0, DateTime.MinValue, DefaultTtl, exists: false, "租约文件不存在");

        /// <summary>读取竞争 — 视为活跃，按 TTL 正常计时，不立即接管。</summary>
        public static LeaseSnapshot Busy(string reason) => new(null, 0, DateTime.UtcNow, DefaultTtl, exists: true, reason);

        /// <summary>是否已过期 — 仅存在的租约文件可判定过期；不存在由获取循环重试创建。</summary>
        public bool IsExpired(DateTime utcNow) => Exists && utcNow - LastRenewedUtc > Ttl + ClockSkewTolerance;

        /// <summary>读取并解析租约文件；读取失败返回 <see cref="Busy"/>。</summary>
        public static LeaseSnapshot Inspect(IFileSystem fs, string leasePath) {
            try {
                if (!fs.FileExists(leasePath)) return Missing;

                string text;
                try {
                    using var stream = fs.OpenRead(leasePath);
                    using var reader = new StreamReader(stream);
                    text = reader.ReadToEnd();
                } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                    return Busy("租约文件读取竞争");
                }

                return Parse(text, fs.GetLastWriteTimeUtc(leasePath));
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                return Busy("租约文件探测竞争");
            }
        }

        /// <summary>解析租约文件内容 — 完整行才参与判定，尾部半行忽略。</summary>
        public static LeaseSnapshot Parse(string text, DateTime fileWriteUtc) {
            string? token = null;
            var pid = 0;
            var ttl = DefaultTtl;
            long lastTicks = 0;

            var remaining = text.AsSpan();
            while (true) {
                var newline = remaining.IndexOf('\n');
                if (newline < 0) break;

                var line = remaining[..newline].TrimEnd('\r');
                remaining = remaining[(newline + 1)..];

                if (line.StartsWith("lease|1|", StringComparison.Ordinal)) {
                    ParseHeader(line[8..], out token, out pid, out ttl, out lastTicks);
                } else if (line.StartsWith("renew|", StringComparison.Ordinal) && long.TryParse(line[6..], out var renewTicks)) {
                    lastTicks = renewTicks;
                }
            }

            var lastRenewedUtc = lastTicks > 0 && lastTicks <= DateTime.MaxValue.Ticks
                ? new DateTime(lastTicks, DateTimeKind.Utc)
                : fileWriteUtc.ToUniversalTime();

            return token is null && lastTicks == 0
                // 创建方尚未写出完整 header（或正在心跳压缩重写）— 用文件时间戳兜底
                ? new LeaseSnapshot(null, 0, lastRenewedUtc, ttl, exists: true, "租约文件无完整记录")
                : new LeaseSnapshot(token, pid, lastRenewedUtc, ttl, exists: true, "ok");
        }

        private static void ParseHeader(ReadOnlySpan<char> fields, out string? token, out int pid, out TimeSpan ttl, out long ticks) {
            token = null;
            pid = 0;
            ttl = DefaultTtl;
            ticks = 0;

            var tokenEnd = fields.IndexOf('|');
            if (tokenEnd <= 0) return;
            token = fields[..tokenEnd].ToString();
            fields = fields[(tokenEnd + 1)..];

            var pidEnd = fields.IndexOf('|');
            if (pidEnd <= 0) return;
            _ = int.TryParse(fields[..pidEnd], out pid);
            fields = fields[(pidEnd + 1)..];

            var ttlEnd = fields.IndexOf('|');
            if (ttlEnd <= 0) return;
            if (long.TryParse(fields[..ttlEnd], out var ttlMs) && ttlMs > 0) {
                ttl = TimeSpan.FromMilliseconds(ttlMs);
            }
            fields = fields[(ttlEnd + 1)..];

            _ = long.TryParse(fields, out ticks);
        }

        /// <inheritdoc />
        public override string ToString() => $"{_reason}; token={Token} pid={OwnerProcessId} lastRenewedUtc={LastRenewedUtc:O}";
    }
}
