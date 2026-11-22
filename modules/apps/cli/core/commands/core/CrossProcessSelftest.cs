namespace JoinCode;

/// <summary>
/// 跨进程租约真机自检 — 三个真实 jcc 进程竞争同一 PithosDB 数据目录，验证：
/// <para>1. 磁盘租约独占：竞争期所有权日志严格 enter/exit 交替，无重叠持有。</para>
/// <para>2. 非阻塞降级：拿不到租约的进程立即降级内存，获取耗时 &lt; 1s。</para>
/// <para>3. 崩溃接管：强制结束 owner 进程后，等待 TTL 其他进程接管；崩溃前写入的数据可恢复。</para>
/// <para>入口：<c>jcc --selftest-crossproc</c>（父进程编排）与 <c>jcc --selftest-crossproc worker ...</c>（子进程）。</para>
/// </summary>
internal static class CrossProcessSelftest {
    private const int WorkerCount = 3;
    private const int CompeteSeconds = 8;
    private const int HoldMs = 60;
    private static readonly TimeSpan ChildTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan AcquiredWaitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TakeoverWaitTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan MinTakeoverDelay = TimeSpan.FromSeconds(8);

    private static IFileSystem Fs => IO.FileSystem.FileSystemFactory.Create();

    /// <summary>父进程 — 编排竞争 + 崩溃接管两阶段验收并输出 PASS/FAIL。</summary>
    public static async Task<int> RunParentAsync() {
        var fs = Fs;
        var root = Path.Combine(Path.GetTempPath(), $"jcc_crossproc_{Guid.NewGuid():N}");
        var dataDir = Path.Combine(root, "kvstore");
        var ownershipLog = Path.Combine(root, "ownership.log");

        Console.WriteLine($"[crossproc] start root={root} workers={WorkerCount} compete={CompeteSeconds}s hold={HoldMs}ms");
        try {
            fs.CreateDirectory(root);

            var competePass = await RunCompetitionAsync(fs, dataDir, ownershipLog);
            var takeoverPass = await RunCrashTakeoverAsync(fs, dataDir);

            var pass = competePass && takeoverPass;
            Console.WriteLine(pass
                ? "[crossproc] PASS"
                : "[crossproc] FAIL compete=" + competePass + " takeover=" + takeoverPass);
            return pass ? 0 : 1;
        } catch (Exception ex) {
            Console.WriteLine($"[crossproc] FAIL {ex.GetType().Name}: {ex.Message}");
            return 1;
        } finally {
            TryDeleteDirectory(fs, root);
        }
    }

    /// <summary>子进程 — 按 mode 参与竞争或持有租约等待被杀。</summary>
    public static async Task<int> RunWorkerAsync(string mode, string dataDir, string workerId, string durationMs) {
        try {
            return mode switch {
                "compete" => await RunCompeteWorkerAsync(dataDir, workerId, int.Parse(durationMs, System.Globalization.CultureInfo.InvariantCulture)),
                "crash" => await RunCrashWorkerAsync(dataDir, workerId),
                _ => Fail($"未知 worker mode: {mode}"),
            };
        } catch (Exception ex) {
            return Fail($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task<bool> RunCompetitionAsync(IFileSystem fs, string dataDir, string ownershipLog) {
        Console.WriteLine("[crossproc] phase=competition");
        var workers = new List<(System.Diagnostics.Process Process, System.Collections.Concurrent.ConcurrentQueue<string> Output)>();
        try {
            for (var i = 0; i < WorkerCount; i++) {
                workers.Add(SpawnWorker("compete", dataDir, $"w{i}", (CompeteSeconds * 1000).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            var exitCodes = await Task.WhenAll(workers.Select(w => WaitForExitAsync(w.Process, ChildTimeout)));
            var output = workers.SelectMany(w => w.Output.ToArray()).ToList();
            foreach (var line in output) Console.WriteLine("  " + line);

            if (exitCodes.Any(code => code != 0)) {
                Console.WriteLine($"[crossproc] competition FAIL 子进程退出码: {string.Join(",", exitCodes)}");
                return false;
            }

            var acquires = output.Count(l => l.Contains("acquire ok", StringComparison.Ordinal));
            var degrades = output.Count(l => l.Contains("degrade", StringComparison.Ordinal));
            var maxDegradeMs = output
                .Where(l => l.Contains("degrade", StringComparison.Ordinal))
                .Select(ParseAcquireMs)
                .DefaultIfEmpty(0)
                .Max();
            Console.WriteLine($"[crossproc] competition acquires={acquires} degrades={degrades} maxDegradeAcquireMs={maxDegradeMs}");
            if (acquires < WorkerCount) {
                Console.WriteLine("[crossproc] competition FAIL 磁盘租约获取次数不足");
                return false;
            }
            if (degrades == 0) {
                Console.WriteLine("[crossproc] competition FAIL 未观察到竞争降级（三进程未真正并发）");
                return false;
            }
            if (maxDegradeMs >= 1000) {
                Console.WriteLine("[crossproc] competition FAIL 降级获取阻塞超过 1s（应非阻塞）");
                return false;
            }

            var entered = await ValidateOwnershipLogAsync(fs, ownershipLog, dataDir);
            Console.WriteLine(entered
                ? $"[crossproc] competition ownership log alternating OK ({acquires} acquisitions)"
                : "[crossproc] competition FAIL ownership log 出现重叠持有或数据缺失");
            return entered;
        } finally {
            foreach (var (process, _) in workers) process.DisposeSafe();
        }
    }

    private static async Task<bool> RunCrashTakeoverAsync(IFileSystem fs, string dataDir) {
        Console.WriteLine("[crossproc] phase=crash-takeover");
        var worker = SpawnWorker("crash", dataDir, "crash", "0");
        try {
            var acquiredLine = await WaitForLineAsync(worker.Output, "ACQUIRED", AcquiredWaitTimeout);
            if (acquiredLine is null) {
                Console.WriteLine("[crossproc] takeover FAIL 崩溃测试进程未获取租约");
                return false;
            }
            Console.WriteLine("  " + acquiredLine);

            // 等待心跳续期 2 次（间隔 5s）后再强杀 — 验收日志需包含 renew 时间线
            await Task.Delay(TimeSpan.FromSeconds(11)).ConfigureAwait(false);
            Console.WriteLine("[crossproc] owner held 11s (renew x2), killing...");
            worker.Process.Kill(entireProcessTree: true);
            await worker.Process.WaitForExitAsync().ConfigureAwait(false);
            foreach (var line in worker.Output.Where(l => l.Contains("renew", StringComparison.Ordinal) || l.StartsWith("[stderr]", StringComparison.Ordinal))) {
                Console.WriteLine("  " + line);
            }
            var killedAt = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine($"[crossproc] killed owner pid={worker.Process.Id}, waiting TTL takeover...");

            var factory = new JoinCode.Persistence.KvStore.PithosKvStoreLeaseFactory(dataDir);
            IKvStoreLease? lease = null;
            while (killedAt.Elapsed < TakeoverWaitTimeout) {
                var candidate = await factory.AcquireAsync().ConfigureAwait(false);
                if (!candidate.IsInMemory) {
                    lease = candidate;
                    break;
                }
                candidate.Dispose();
                await Task.Delay(500).ConfigureAwait(false);
            }

            if (lease is null) {
                Console.WriteLine("[crossproc] takeover FAIL TTL 内未接管");
                return false;
            }

            using (lease) {
                Console.WriteLine($"[crossproc] takeover acquired delayMs={killedAt.ElapsedMilliseconds} (TTL 等待)");
                if (killedAt.Elapsed < MinTakeoverDelay) {
                    Console.WriteLine("[crossproc] takeover FAIL 接管过快（应等待 TTL，可能读到了错误的租约状态）");
                    return false;
                }

                var before = await lease.GetAsync(K("crash:before")).ConfigureAwait(false);
                if (before is null) {
                    Console.WriteLine("[crossproc] takeover FAIL 崩溃前写入的 crash:before 丢失");
                    return false;
                }
                Console.WriteLine($"  crash:before={S(before)} recovered");
                await lease.PutAsync(K("crash:after"), K("after-value")).ConfigureAwait(false);
            }

            using var verify = await factory.AcquireAsync().ConfigureAwait(false);
            if (verify.IsInMemory) {
                Console.WriteLine("[crossproc] takeover FAIL 释放后无法立即重新获取磁盘租约");
                return false;
            }
            var after = await verify.GetAsync(K("crash:after")).ConfigureAwait(false);
            var beforeAgain = await verify.GetAsync(K("crash:before")).ConfigureAwait(false);
            var pass = beforeAgain is not null && after is not null && S(after) == "after-value";
            Console.WriteLine(pass
                ? "[crossproc] takeover data integrity OK (restart recovery + new write survived)"
                : "[crossproc] takeover FAIL 数据完整性校验失败");
            return pass;
        } finally {
            worker.Process.DisposeSafe();
        }
    }

    private static async Task<bool> ValidateOwnershipLogAsync(IFileSystem fs, string ownershipLog, string dataDir) {
        if (!fs.FileExists(ownershipLog)) {
            Console.WriteLine("[crossproc] FAIL 所有权日志缺失");
            return false;
        }

        var lines = await fs.ReadAllLinesAsync(ownershipLog).ConfigureAwait(false);
        var inside = false;
        string? owner = null;
        var enters = 0;
        foreach (var line in lines) {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split('|');
            if (parts.Length < 3) return false;

            switch (parts[0]) {
                case "enter":
                if (inside) {
                    Console.WriteLine($"[crossproc] FAIL 重叠持有: 上一个 owner={owner} 未 exit，新 owner={parts[1]} enter");
                    return false;
                }
                inside = true;
                owner = $"{parts[1]}:{parts[2]}";
                enters++;
                break;
                case "exit":
                if (!inside || owner != $"{parts[1]}:{parts[2]}") {
                    Console.WriteLine($"[crossproc] FAIL 所有权日志不匹配: inside={inside} owner={owner} exit={parts[1]}:{parts[2]}");
                    return false;
                }
                inside = false;
                owner = null;
                break;
            }
        }

        if (inside) {
            Console.WriteLine($"[crossproc] FAIL 所有权日志悬空 enter: {owner}");
            return false;
        }

        var factory = new JoinCode.Persistence.KvStore.PithosKvStoreLeaseFactory(dataDir);
        using var lease = await factory.AcquireAsync().ConfigureAwait(false);
        if (lease.IsInMemory) {
            Console.WriteLine("[crossproc] FAIL 校验阶段无法获取磁盘租约");
            return false;
        }

        var scanned = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var (key, _) in lease.ScanAsync().ConfigureAwait(false)) {
            scanned.Add(S(key));
        }

        var missing = lines
            .Where(l => l.StartsWith("enter|", StringComparison.Ordinal))
            .Select(l => l.Split('|'))
            .Select(parts => $"op:{parts[1]}:{parts[2]}")
            .Where(key => !scanned.Contains(key))
            .ToList();
        if (missing.Count > 0) {
            Console.WriteLine($"[crossproc] FAIL 所有权日志有 {missing.Count} 个操作在存储中缺失（如 {missing[0]}）");
            return false;
        }

        if (scanned.Count != enters) {
            Console.WriteLine($"[crossproc] FAIL 存储键数 {scanned.Count} 与 acquire 次数 {enters} 不一致");
            return false;
        }

        return true;
    }

    private static async Task<int> RunCompeteWorkerAsync(string dataDir, string workerId, int durationMs) {
        var fs = Fs;
        var ownershipLog = Path.Combine(Path.GetDirectoryName(dataDir) ?? dataDir, "ownership.log");
        var factory = new JoinCode.Persistence.KvStore.PithosKvStoreLeaseFactory(dataDir);
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(durationMs);
        var acquires = 0;
        var degrades = 0;
        long maxAcquireMs = 0;

        while (DateTime.UtcNow < deadline) {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using (var lease = await factory.AcquireAsync().ConfigureAwait(false)) {
                sw.Stop();
                if (lease.IsInMemory) {
                    degrades++;
                    maxAcquireMs = Math.Max(maxAcquireMs, sw.ElapsedMilliseconds);
                    Console.WriteLine($"[worker {workerId}] degrade acquireMs={sw.ElapsedMilliseconds} at={DateTime.UtcNow.Ticks}");
                    await Task.Delay(Random.Shared.Next(5, 120)).ConfigureAwait(false);
                    continue;
                }

                var opId = Guid.NewGuid().ToString("N")[..12];
                await lease.PutAsync(K($"op:{workerId}:{opId}"), K($"{workerId}|{opId}")).ConfigureAwait(false);
                await fs.AppendAllTextAsync(ownershipLog, $"enter|{workerId}|{opId}|{DateTime.UtcNow.Ticks}\n").ConfigureAwait(false);
                acquires++;
                Console.WriteLine($"[worker {workerId}] acquire ok op={opId} acquireMs={sw.ElapsedMilliseconds} at={DateTime.UtcNow.Ticks}");
                await Task.Delay(HoldMs).ConfigureAwait(false);
                await fs.AppendAllTextAsync(ownershipLog, $"exit|{workerId}|{opId}|{DateTime.UtcNow.Ticks}\n").ConfigureAwait(false);
            }

            // 释放后让出竞争窗口 — 否则同一进程立即重取会饿死其他进程，验收需覆盖轮转持有
            await Task.Delay(Random.Shared.Next(10, 90)).ConfigureAwait(false);
        }

        Console.WriteLine($"[worker {workerId}] done acquires={acquires} degrades={degrades} maxDegradeAcquireMs={maxAcquireMs}");
        return 0;
    }

    private static async Task<int> RunCrashWorkerAsync(string dataDir, string workerId) {
        var factory = new JoinCode.Persistence.KvStore.PithosKvStoreLeaseFactory(dataDir);
        using var lease = await factory.AcquireAsync().ConfigureAwait(false);
        if (lease.IsInMemory) {
            Console.WriteLine($"[worker {workerId}] REFUSED 未拿到磁盘租约");
            return 2;
        }

        await lease.PutAsync(K("crash:before"), K($"before|{workerId}|{DateTime.UtcNow.Ticks}")).ConfigureAwait(false);
        Console.WriteLine($"[worker {workerId}] ACQUIRED pid={Environment.ProcessId} dataDir={dataDir}");
        Console.Out.Flush();
        await Task.Delay(TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        Console.WriteLine($"[worker {workerId}] NOT-KILLED");
        return 3;
    }

    private static (System.Diagnostics.Process Process, System.Collections.Concurrent.ConcurrentQueue<string> Output) SpawnWorker(string mode, string dataDir, string workerId, string durationMs) {
        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Environment.ProcessPath 为空，无法定位 jcc.exe");
        var psi = new System.Diagnostics.ProcessStartInfo {
            FileName = exePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("--selftest-crossproc");
        psi.ArgumentList.Add("worker");
        psi.ArgumentList.Add(mode);
        psi.ArgumentList.Add(dataDir);
        psi.ArgumentList.Add(workerId);
        psi.ArgumentList.Add(durationMs);

        var output = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var process = new System.Diagnostics.Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.Enqueue(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.Enqueue("[stderr] " + e.Data); };
        if (!process.Start()) throw new InvalidOperationException($"无法启动子进程: {exePath}");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return (process, output);
    }

    private static async Task<int> WaitForExitAsync(System.Diagnostics.Process process, TimeSpan timeout) {
        using var cts = new CancellationTokenSource(timeout);
        try {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return process.ExitCode;
        } catch (OperationCanceledException) {
            try { process.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException) { Console.WriteLine($"[crossproc] kill 超时进程失败: {ex.Message}"); }
            return -1;
        }
    }

    private static async Task<string?> WaitForLineAsync(System.Collections.Concurrent.ConcurrentQueue<string> output, string marker, TimeSpan timeout) {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < timeout) {
            if (output.FirstOrDefault(l => l.Contains(marker, StringComparison.Ordinal)) is { } line) return line;
            await Task.Delay(100).ConfigureAwait(false);
        }
        return null;
    }

    private static long ParseAcquireMs(string line) {
        const string marker = "acquireMs=";
        var index = line.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0) return 0;
        var value = line[(index + marker.Length)..];
        var end = value.IndexOf(' ');
        return long.TryParse(end < 0 ? value : value[..end], out var ms) ? ms : 0;
    }

    private static void TryDeleteDirectory(IFileSystem fs, string dir) {
        try {
            if (fs.DirectoryExists(dir)) fs.DeleteDirectory(dir, recursive: true);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            Console.WriteLine($"[crossproc] cleanup 失败（可手工清理）: {dir} {ex.Message}");
        }
    }

    private static int Fail(string message) {
        Console.WriteLine($"[worker] ERROR {message}");
        return 1;
    }

    private static byte[] K(string s) => Encoding.UTF8.GetBytes(s);
    private static string S(byte[] b) => Encoding.UTF8.GetString(b);
}
