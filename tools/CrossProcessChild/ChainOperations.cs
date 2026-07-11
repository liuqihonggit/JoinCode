namespace CrossProcessChild;

/// <summary>
/// 5条跨进程链路的实际操作实现
/// 每条链路通过 FileLockService + 真实应用类验证跨进程并发安全性
/// </summary>
public static class ChainOperations
{
    private static readonly PhysicalFileSystem Fs = new();

    // ─────────────────────────────────────────────────────────────
    // LINK-001: FileLockService 跨进程互斥验证
    // ─────────────────────────────────────────────────────────────

    public static async Task RunLink001Async(string workingDir, int processId, int writeCount, TelemetryService telemetry)
    {
        using var span = telemetry.StartSpan("chain.link001.filelock", TelemetrySpanKind.Internal);
        span.SetTag("chain.id", "LINK-001");
        span.SetTag("process.id", processId.ToString());
        span.SetTag("write.count", writeCount);

        var filePath = Path.Combine(workingDir, "link001-concurrent.txt");
        var lockTimeout = TimeSpan.FromSeconds(15);

        for (var i = 0; i < writeCount; i++)
        {
            using var writeSpan = telemetry.StartSpan("chain.link001.write", TelemetrySpanKind.Internal, span);
            writeSpan.SetTag("write.index", i);

            var result = await AsyncFileLock.FileLockService.AcquireAsync(filePath, lockTimeout).ConfigureAwait(false);
            if (!result.Success)
            {
                writeSpan.SetStatus(TelemetryStatusCode.Error, "Lock acquisition failed");
                throw new TimeoutException($"Process {processId} failed to acquire lock for write {i}");
            }

            var lockObj = result.Lock!;
            await using (lockObj.ConfigureAwait(false))
            {
                var existing = Fs.FileExists(filePath)
                    ? await Fs.ReadAllTextAsync(filePath).ConfigureAwait(false)
                    : string.Empty;

                var newLine = $"proc-{processId}-write-{i}";
                var content = string.IsNullOrEmpty(existing)
                    ? newLine + "\n"
                    : existing + newLine + "\n";

                await Fs.WriteAllTextAsync(filePath, content).ConfigureAwait(false);
                writeSpan.SetTag("line", newLine);
            }
        }

        span.SetStatus(TelemetryStatusCode.Ok);
    }

    // ─────────────────────────────────────────────────────────────
    // LINK-002: HighWaterMarkManager 跨进程原子递增
    // ─────────────────────────────────────────────────────────────

    public static async Task RunLink002Async(string workingDir, int processId, int incrementCount, TelemetryService telemetry)
    {
        using var span = telemetry.StartSpan("chain.link002.hwm", TelemetrySpanKind.Internal);
        span.SetTag("chain.id", "LINK-002");
        span.SetTag("process.id", processId.ToString());
        span.SetTag("increment.count", incrementCount);

        var taskDir = Path.Combine(workingDir, "link002-hwm");
        Fs.CreateDirectory(taskDir);

        var options = TaskDirectoryOptionsBuilder.Create()
            .WithTaskDirectoryPath(taskDir)
            .Build();

        var manager = new HighWaterMarkManager(Fs, options);
        var results = new List<int>();

        for (var i = 0; i < incrementCount; i++)
        {
            using var incSpan = telemetry.StartSpan("chain.link002.increment", TelemetrySpanKind.Internal, span);
            var value = await manager.IncrementAndGetAsync().ConfigureAwait(false);
            results.Add(value);
            incSpan.SetTag("value", value);
        }

        var resultFile = Path.Combine(workingDir, $"link002-result-{processId}.json");
        var resultJson = JsonSerializer.Serialize(results, CrossProcessJsonContext.Default.ListInt32);
        await Fs.WriteAllTextAsync(resultFile, resultJson).ConfigureAwait(false);

        span.SetTag("final.value", results.Count > 0 ? results[^1] : 0);
        span.SetStatus(TelemetryStatusCode.Ok);
    }

    // ─────────────────────────────────────────────────────────────
    // LINK-003: ConfigLoader 并发读写 auth.json
    // ─────────────────────────────────────────────────────────────

    public static async Task RunLink003Async(string workingDir, int processId, int writeCount, TelemetryService telemetry)
    {
        using var span = telemetry.StartSpan("chain.link003.config", TelemetrySpanKind.Internal);
        span.SetTag("chain.id", "LINK-003");
        span.SetTag("process.id", processId.ToString());
        span.SetTag("write.count", writeCount);

        var authDir = Path.Combine(workingDir, "link003-config");
        Fs.CreateDirectory(authDir);
        var authPath = Path.Combine(authDir, "auth.json");

        var lockTimeout = TimeSpan.FromSeconds(10);

        for (var i = 0; i < writeCount; i++)
        {
            using var rwSpan = telemetry.StartSpan("chain.link003.read-modify-write", TelemetrySpanKind.Internal, span);
            rwSpan.SetTag("iteration", i);

            var result = await AsyncFileLock.FileLockService.AcquireAsync(authPath, lockTimeout).ConfigureAwait(false);
            if (!result.Success)
            {
                rwSpan.SetStatus(TelemetryStatusCode.Error, "Lock timeout");
                throw new TimeoutException($"Process {processId} failed to acquire lock for config write {i}");
            }

            var lockObj = result.Lock!;
            await using (lockObj.ConfigureAwait(false))
            {
                var json = await ReadFileWithRetryAsync(authPath).ConfigureAwait(false);
                var data = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.DictionaryStringString)
                    ?? new Dictionary<string, string>();

                var key = $"provider-{processId}";
                data[key] = $"key-{processId}-{i}";

                var output = JsonSerializer.Serialize(data, ConfigIndentedJsonContext.Default.DictionaryStringString);
                await WriteFileWithRetryAsync(authPath, output).ConfigureAwait(false);

                rwSpan.SetTag("key", key);
                rwSpan.SetTag("value", data[key]);
            }
        }

        var verifyPath = Path.Combine(workingDir, $"link003-result-{processId}.json");
        var verifyJson = await Fs.ReadAllTextAsync(authPath).ConfigureAwait(false);
        await Fs.WriteAllTextAsync(verifyPath, verifyJson).ConfigureAwait(false);

        span.SetStatus(TelemetryStatusCode.Ok);
    }

    // ─────────────────────────────────────────────────────────────
    // LINK-004: TranscriptFileWriter 并发追加 JSONL
    // ─────────────────────────────────────────────────────────────

    public static async Task RunLink004Async(string workingDir, int processId, int appendCount, TelemetryService telemetry)
    {
        using var span = telemetry.StartSpan("chain.link004.jsonl", TelemetrySpanKind.Internal);
        span.SetTag("chain.id", "LINK-004");
        span.SetTag("process.id", processId.ToString());
        span.SetTag("append.count", appendCount);

        var jsonlDir = Path.Combine(workingDir, "link004-jsonl");
        Fs.CreateDirectory(jsonlDir);
        var jsonlPath = Path.Combine(jsonlDir, "transcript.jsonl");

        try
        {
            if (!Fs.FileExists(jsonlPath))
            {
                await Fs.WriteAllTextAsync(jsonlPath, string.Empty).ConfigureAwait(false);
            }
        }
        catch (IOException ex)
        {
            Console.WriteLine($"[Child] WARN: JSONL file creation race: {ex.Message}");
        }

        var lockTimeout = TimeSpan.FromSeconds(15);

        for (var i = 0; i < appendCount; i++)
        {
            using var appendSpan = telemetry.StartSpan("chain.link004.append", TelemetrySpanKind.Internal, span);
            appendSpan.SetTag("index", i);

            var result = await AsyncFileLock.FileLockService.AcquireAsync(jsonlPath, lockTimeout).ConfigureAwait(false);
            if (!result.Success)
            {
                appendSpan.SetStatus(TelemetryStatusCode.Error, "Lock timeout");
                throw new TimeoutException($"Process {processId} failed to acquire lock for JSONL append {i}");
            }

            var lockObj = result.Lock!;
            await using (lockObj.ConfigureAwait(false))
            {
                var entry = new TranscriptEntry
                {
                    Role = "user",
                    Content = $"proc-{processId}-entry-{i}",
                    Timestamp = DateTime.UtcNow
                };
                var line = JsonSerializer.Serialize(entry, TranscriptJsonContext.Default.TranscriptEntry);
                await AppendFileWithRetryAsync(jsonlPath, line).ConfigureAwait(false);
                appendSpan.SetTag("content", entry.Content);
            }
        }

        var verifyPath = Path.Combine(workingDir, $"link004-result-{processId}.json");
        var lineCount = 0;
        if (Fs.FileExists(jsonlPath))
        {
            var lines = await Fs.ReadAllLinesAsync(jsonlPath).ConfigureAwait(false);
            lineCount = lines.Length;
        }
        await Fs.WriteAllTextAsync(verifyPath, lineCount.ToString()).ConfigureAwait(false);

        span.SetTag("total.lines", lineCount);
        span.SetStatus(TelemetryStatusCode.Ok);
    }

    // ─────────────────────────────────────────────────────────────
    // LINK-005: Worktree 多 Agent 读写文件
    // 2个 agent 各自创建 worktree，写文件并 git commit
    // ─────────────────────────────────────────────────────────────

    public static async Task RunLink005Async(string workingDir, int processId, int writeCount, TelemetryService telemetry)
    {
        using var span = telemetry.StartSpan("chain.link005.worktree", TelemetrySpanKind.Internal);
        span.SetTag("chain.id", "LINK-005");
        span.SetTag("process.id", processId.ToString());
        span.SetTag("write.count", writeCount);

        var agentId = $"agent-{processId}";
        var worktreePath = Path.Combine(workingDir, ".worktrees", agentId);
        var branchName = $"worktree/{agentId}";

        using (var createSpan = telemetry.StartSpan("chain.link005.worktree.create", TelemetrySpanKind.Internal, span))
        {
            if (!Directory.Exists(worktreePath))
            {
                var createResult = await RunGitAsync(workingDir,
                    $"worktree add -b {branchName} \"{worktreePath}\"").ConfigureAwait(false);
                if (!createResult.Success)
                {
                    createSpan.SetStatus(TelemetryStatusCode.Error, createResult.Error);
                    throw new InvalidOperationException($"Failed to create worktree for {agentId}: {createResult.Error}");
                }
            }
            createSpan.SetStatus(TelemetryStatusCode.Ok);
        }

        for (var i = 0; i < writeCount; i++)
        {
            using var writeSpan = telemetry.StartSpan("chain.link005.write", TelemetrySpanKind.Internal, span);
            writeSpan.SetTag("index", i);

            var fileName = $"file-{processId}-{i}.txt";
            var filePath = Path.Combine(worktreePath, fileName);
            var content = $"Agent {processId} wrote file {i} at {DateTime.UtcNow:O}\n";
            await File.WriteAllTextAsync(filePath, content).ConfigureAwait(false);

            var addResult = await RunGitAsync(worktreePath, $"add {fileName}").ConfigureAwait(false);
            if (!addResult.Success)
            {
                writeSpan.SetStatus(TelemetryStatusCode.Error, addResult.Error);
                throw new InvalidOperationException($"Failed to git add: {addResult.Error}");
            }

            var commitResult = await RunGitAsync(worktreePath,
                $"commit -m \"{agentId}: add {fileName}\"").ConfigureAwait(false);
            if (!commitResult.Success)
            {
                writeSpan.SetStatus(TelemetryStatusCode.Error, commitResult.Error);
                throw new InvalidOperationException($"Failed to git commit: {commitResult.Error}");
            }

            writeSpan.SetTag("file", fileName);
            writeSpan.SetStatus(TelemetryStatusCode.Ok);
        }

        using (var verifySpan = telemetry.StartSpan("chain.link005.verify", TelemetrySpanKind.Internal, span))
        {
            var logResult = await RunGitAsync(worktreePath, "log --oneline").ConfigureAwait(false);
            var commitCount = logResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            verifySpan.SetTag("commit.count", commitCount);
            verifySpan.SetStatus(TelemetryStatusCode.Ok);
        }

        var resultFile = Path.Combine(workingDir, $"link005-result-{processId}.json");
        var logResult2 = await RunGitAsync(worktreePath, "log --oneline").ConfigureAwait(false);
        var finalCommitCount = logResult2.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        var resultData = $"{{\"ProcessId\":{processId},\"AgentId\":\"{agentId}\",\"Branch\":\"{branchName}\",\"Commits\":{finalCommitCount}}}";
        await File.WriteAllTextAsync(resultFile, resultData).ConfigureAwait(false);

        span.SetTag("commits", finalCommitCount);
        span.SetStatus(TelemetryStatusCode.Ok);
    }

    private static async Task<GitResult> RunGitAsync(string workDir, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi);
        if (process is null)
            return new GitResult { Success = false, Error = "Failed to start git process" };

        var stdout = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);

        return new GitResult
        {
            Success = process.ExitCode == 0,
            Output = stdout,
            Error = stderr
        };
    }

    private sealed class GitResult
    {
        public bool Success { get; init; }
        public string Output { get; init; } = string.Empty;
        public string Error { get; init; } = string.Empty;
    }

    private static async Task<string> ReadFileWithRetryAsync(string path, int maxRetries = 5)
    {
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                await using var stream = Fs.CreateStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                return await reader.ReadToEndAsync().ConfigureAwait(false);
            }
            catch (IOException) when (attempt < maxRetries)
            {
                await Task.Delay(100 * (attempt + 1)).ConfigureAwait(false);
            }
        }
        return "{}";
    }

    private static async Task WriteFileWithRetryAsync(string path, string content, int maxRetries = 10)
    {
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await using (var stream = Fs.CreateStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await using (var writer = new StreamWriter(stream))
                {
                    await writer.WriteAsync(content).ConfigureAwait(false);
                }

                for (var moveAttempt = 0; moveAttempt <= 10; moveAttempt++)
                {
                    try
                    {
                        File.Move(tempPath, path, overwrite: true);
                        return;
                    }
                    catch (IOException) when (moveAttempt < 10)
                    {
                        await Task.Delay(100 * (moveAttempt + 1)).ConfigureAwait(false);
                    }
                }

                if (File.Exists(tempPath)) File.Delete(tempPath);
                throw new IOException($"Failed to move temp file to {path} after retries");
            }
            catch (IOException) when (attempt < maxRetries)
            {
                await Task.Delay(100 * (attempt + 1)).ConfigureAwait(false);
            }
        }
    }

    private static async Task AppendFileWithRetryAsync(string path, string line, int maxRetries = 5)
    {
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                await using var stream = Fs.CreateStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                await using var writer = new StreamWriter(stream);
                await writer.WriteLineAsync(line.AsMemory()).ConfigureAwait(false);
                return;
            }
            catch (IOException) when (attempt < maxRetries)
            {
                await Task.Delay(100 * (attempt + 1)).ConfigureAwait(false);
            }
        }
    }
}

[JsonSerializable(typeof(List<int>))]
[JsonSerializable(typeof(int))]
internal sealed partial class CrossProcessJsonContext : JsonSerializerContext;
