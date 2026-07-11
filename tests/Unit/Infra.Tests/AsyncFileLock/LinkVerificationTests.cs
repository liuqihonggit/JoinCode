namespace AsyncFileLock;

/// <summary>
/// 多链路验证测试 — 每条链路验证一个关键的并发文件操作场景
/// 链路ID格式: LINK-{NNN}
///
/// LINK-001: FileLockService 并发互斥 — 多任务并发写同一文件
/// LINK-002: HighWaterMarkManager 原子递增 — 多任务并发递增无跳号
/// LINK-003: 并发 JSONL 追加 — 多任务追加 JSONL 无损坏
/// LINK-004: FileLockService 批量锁死锁预防 — 多文件锁按序获取
/// LINK-005: FileShare.ReadWrite 并发读写 — 读取时不阻塞写入
/// LINK-006: FileLockService 锁超时 — 持有锁时请求应超时
/// LINK-007: HighWaterMarkManager 读-改-写原子性 — Update 不丢失
/// </summary>
[CollectionDefinition(nameof(LinkVerificationCollection), DisableParallelization = true)]
public sealed class LinkVerificationCollection;

[Collection(nameof(LinkVerificationCollection))]
public sealed class LinkVerificationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly IFileSystem _fs;
    private readonly string _testRoot;

    public LinkVerificationTests(ITestOutputHelper output)
    {
        _output = output;
        _fs = new PhysicalFileSystem();
        _testRoot = Path.Combine(Path.GetTempPath(), $"JoinCode_LinkTest_{Guid.NewGuid():N}");
        _fs.CreateDirectory(_testRoot);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        try
        {
            if (_fs.DirectoryExists(_testRoot))
                _fs.DeleteDirectory(_testRoot, recursive: true);
        }
        catch (IOException ex)
        {
            _output.WriteLine($"Cleanup skipped: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    // ─────────────────────────────────────────────────────────────
    // LINK-001: FileLockService 并发互斥
    // 场景: 多个任务并发写同一文件，验证锁互斥保护数据完整性
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task LINK_001_FileLockService_ConcurrentWrite_ShouldSerializeAccess()
    {
        const string linkId = "LINK-001";
        var filePath = Path.Combine(_testRoot, "concurrent-write.txt");
        const int taskCount = 10;
        const int writesPerTask = 50;

        _output.WriteLine($"[{linkId}] Starting: {taskCount} tasks x {writesPerTask} writes each");

        var tasks = Enumerable.Range(0, taskCount).Select(taskId =>
            WriteWithLockAsync(filePath, taskId, writesPerTask));

        await Task.WhenAll(tasks).ConfigureAwait(true);

        var lines = await _fs.ReadAllLinesAsync(filePath).ConfigureAwait(true);
        _output.WriteLine($"[{linkId}] Total lines written: {lines.Length}");

        lines.Length.Should().Be(taskCount * writesPerTask,
            $"each of {taskCount} tasks should write {writesPerTask} lines");

        foreach (var line in lines)
        {
            line.Should().MatchRegex(@"^task-\d+-write-\d+$",
                "every line should be a complete write entry, no partial or mixed lines");
        }

        _output.WriteLine($"[{linkId}] PASSED: All {lines.Length} lines are complete and properly formatted");
    }

    private async Task WriteWithLockAsync(string filePath, int taskId, int writeCount)
    {
        for (var i = 0; i < writeCount; i++)
        {
            var result = await FileLockService.AcquireAsync(filePath, TimeSpan.FromSeconds(10)).ConfigureAwait(true);
            result.Success.Should().BeTrue($"lock acquisition should succeed for task {taskId} write {i}");

            await using (result.Lock!)
            {
                var existing = _fs.FileExists(filePath)
                    ? await _fs.ReadAllTextAsync(filePath).ConfigureAwait(true)
                    : string.Empty;

                var newLine = $"task-{taskId}-write-{i}";
                var content = string.IsNullOrEmpty(existing)
                    ? newLine + "\n"
                    : existing + newLine + "\n";

                await _fs.WriteAllTextAsync(filePath, content).ConfigureAwait(true);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // LINK-002: HighWaterMarkManager 原子递增
    // 场景: 多个任务并发调用 IncrementAndGetAsync，验证值单调递增无跳号
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task LINK_002_HighWaterMarkManager_ConcurrentIncrement_ShouldBeMonotonic()
    {
        const string linkId = "LINK-002";
        var taskDir = Path.Combine(_testRoot, "hwm");
        _fs.CreateDirectory(taskDir);

        var options = TaskDirectoryOptionsBuilder.Create()
            .WithTaskDirectoryPath(taskDir)
            .Build();

        var manager = new HighWaterMarkManager(_fs, options);
        const int incrementCount = 20;
        const int taskCount = 5;

        _output.WriteLine($"[{linkId}] Starting: {taskCount} tasks x {incrementCount} increments each");

        var results = new ConcurrentBag<int>();

        var tasks = Enumerable.Range(0, taskCount).Select(_ =>
            IncrementMultipleAsync(manager, incrementCount, results));

        await Task.WhenAll(tasks).ConfigureAwait(true);

        var resultList = results.ToList();

        resultList.Count.Should().Be(taskCount * incrementCount,
            $"should have {taskCount * incrementCount} unique increment results");

        resultList.Distinct().Count().Should().Be(resultList.Count,
            "all increment results should be unique, no duplicates");

        var sorted = resultList.OrderBy(v => v).ToList();
        for (var i = 0; i < sorted.Count; i++)
        {
            sorted[i].Should().Be(i + 1,
                $"value at position {i} should be {i + 1}, no gaps or duplicates");
        }

        _output.WriteLine($"[{linkId}] PASSED: All {resultList.Count} values are unique and monotonically increasing from 1 to {resultList.Count}");
    }

    private static async Task IncrementMultipleAsync(
        HighWaterMarkManager manager, int count, ConcurrentBag<int> results)
    {
        for (var i = 0; i < count; i++)
        {
            var value = await manager.IncrementAndGetAsync().ConfigureAwait(true);
            results.Add(value);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // LINK-003: 并发 JSONL 追加
    // 场景: 多个任务使用 FileLockService 并发追加 JSONL 行，验证无行损坏
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task LINK_003_ConcurrentJsonlAppend_ShouldNotCorruptLines()
    {
        const string linkId = "LINK-003";
        var filePath = Path.Combine(_testRoot, "concurrent.jsonl");

        await _fs.WriteAllTextAsync(filePath, string.Empty).ConfigureAwait(true);

        const int taskCount = 5;
        const int entriesPerTask = 20;

        _output.WriteLine($"[{linkId}] Starting: {taskCount} tasks x {entriesPerTask} entries each");

        var tasks = Enumerable.Range(0, taskCount).Select(taskId =>
            AppendJsonlWithLockAsync(filePath, taskId, entriesPerTask));

        await Task.WhenAll(tasks).ConfigureAwait(true);

        var lines = await _fs.ReadAllLinesAsync(filePath).ConfigureAwait(true);
        var nonEmptyLines = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

        _output.WriteLine($"[{linkId}] Total non-empty lines: {nonEmptyLines.Count}");

        nonEmptyLines.Count.Should().Be(taskCount * entriesPerTask,
            $"should have {taskCount * entriesPerTask} non-empty lines");

        var validCount = 0;
        foreach (var line in nonEmptyLines)
        {
            var entry = JsonSerializer.Deserialize(line, TranscriptJsonContext.Default.TranscriptEntry);
            entry.Should().NotBeNull("every line should be a valid TranscriptEntry JSON");
            validCount++;
        }

        validCount.Should().Be(nonEmptyLines.Count,
            "all lines should be valid JSON");

        _output.WriteLine($"[{linkId}] PASSED: All {validCount} lines are valid TranscriptEntry JSON");
    }

    private async Task AppendJsonlWithLockAsync(string filePath, int taskId, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var result = await FileLockService.AcquireAsync(filePath, TimeSpan.FromSeconds(10)).ConfigureAwait(true);
            result.Success.Should().BeTrue();

            await using (result.Lock!)
            {
                var entry = new TranscriptEntry
                {
                    Role = "user",
                    Content = $"task-{taskId}-entry-{i}",
                    Timestamp = DateTime.UtcNow
                };
                var line = JsonSerializer.Serialize(entry, TranscriptJsonContext.Default.TranscriptEntry);
                await _fs.AppendAllTextAsync(filePath, line + "\n").ConfigureAwait(true);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // LINK-004: FileLockService 批量锁死锁预防
    // 场景: 两个任务以不同顺序请求相同文件锁，验证不死锁
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task LINK_004_FileLockService_BatchLock_DifferentOrder_ShouldNotDeadlock()
    {
        const string linkId = "LINK-004";
        var fileA = Path.Combine(_testRoot, "file-a.txt");
        var fileB = Path.Combine(_testRoot, "file-b.txt");

        await _fs.WriteAllTextAsync(fileA, "A").ConfigureAwait(true);
        await _fs.WriteAllTextAsync(fileB, "B").ConfigureAwait(true);

        _output.WriteLine($"[{linkId}] Starting: 2 tasks acquiring locks in opposite order");

        var task1 = Task.Run(async () =>
        {
            var result = await FileLockService.AcquireBatchAsync(
                [fileA, fileB], TimeSpan.FromSeconds(10)).ConfigureAwait(true);
            result.Success.Should().BeTrue("task1 should acquire both locks");
            await using (result.Lock!)
            {
                await _fs.WriteAllTextAsync(fileA, "task1-A").ConfigureAwait(true);
                await _fs.WriteAllTextAsync(fileB, "task1-B").ConfigureAwait(true);
                await Task.Delay(100).ConfigureAwait(true);
            }
            return "task1-done";
        });

        var task2 = Task.Run(async () =>
        {
            var result = await FileLockService.AcquireBatchAsync(
                [fileB, fileA], TimeSpan.FromSeconds(10)).ConfigureAwait(true);
            result.Success.Should().BeTrue("task2 should acquire both locks");
            await using (result.Lock!)
            {
                await _fs.WriteAllTextAsync(fileA, "task2-A").ConfigureAwait(true);
                await _fs.WriteAllTextAsync(fileB, "task2-B").ConfigureAwait(true);
            }
            return "task2-done";
        });

        var timeout = TimeSpan.FromSeconds(15);
        var allResults = await Task.WhenAll(task1, task2).WaitAsync(timeout).ConfigureAwait(true);

        allResults.Should().Contain("task1-done");
        allResults.Should().Contain("task2-done");

        var contentA = await _fs.ReadAllTextAsync(fileA).ConfigureAwait(true);
        var contentB = await _fs.ReadAllTextAsync(fileB).ConfigureAwait(true);

        var isTask1Final = contentA == "task1-A" && contentB == "task1-B";
        var isTask2Final = contentA == "task2-A" && contentB == "task2-B";

        (isTask1Final || isTask2Final).Should().BeTrue(
            "files should be in a consistent state written by one task");

        _output.WriteLine($"[{linkId}] PASSED: No deadlock, final state: A={contentA}, B={contentB}");
    }

    // ─────────────────────────────────────────────────────────────
    // LINK-005: FileShare.ReadWrite 并发读写
    // 场景: 一个任务写入文件，另一个任务同时读取，验证读取不被阻塞
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task LINK_005_FileShare_ReadWrite_ConcurrentAccess_ShouldNotBlock()
    {
        const string linkId = "LINK-005";
        var filePath = Path.Combine(_testRoot, "readwrite-test.txt");

        await _fs.WriteAllTextAsync(filePath, "initial\n").ConfigureAwait(true);

        _output.WriteLine($"[{linkId}] Starting: concurrent read/write with FileShare.ReadWrite");

        var writeCompleted = new TaskCompletionSource<string>();
        var readCompleted = new TaskCompletionSource<string>();
        var readStarted = new TaskCompletionSource<bool>();

        // 写入任务
        _ = Task.Run(async () =>
        {
            await using var stream = _fs.CreateStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            await using var writer = new StreamWriter(stream) { AutoFlush = true };

            for (var i = 0; i < 10; i++)
            {
                await writer.WriteLineAsync($"write-line-{i}").ConfigureAwait(true);
                if (i == 2)
                    readStarted.SetResult(true);
                await Task.Delay(50).ConfigureAwait(true);
            }

            writeCompleted.SetResult("write-done");
        });

        // 读取任务
        _ = Task.Run(async () =>
        {
#pragma warning disable VSTHRD003 // TaskCompletionSource is a valid synchronization primitive for coordinating concurrent test tasks
            await readStarted.Task.ConfigureAwait(true);
#pragma warning restore VSTHRD003
            await Task.Delay(100).ConfigureAwait(true);

            using var stream = _fs.CreateStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync().ConfigureAwait(true);

            readCompleted.SetResult(content);
        });

        var timeout = TimeSpan.FromSeconds(10);
        var writeResult = await writeCompleted.Task.WaitAsync(timeout).ConfigureAwait(true);
        var readResult = await readCompleted.Task.WaitAsync(timeout).ConfigureAwait(true);

        writeResult.Should().Be("write-done");
        readResult.Should().Contain("initial", "should see initial content");

        _output.WriteLine($"[{linkId}] PASSED: Concurrent read/write completed without blocking");
        _output.WriteLine($"[{linkId}] Read content length: {readResult.Length} chars");
    }

    // ─────────────────────────────────────────────────────────────
    // LINK-006: FileLockService 锁超时
    // 场景: 一个任务持有锁，另一个任务请求锁应超时
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task LINK_006_FileLockService_LockTimeout_ShouldFail()
    {
        const string linkId = "LINK-006";
        var filePath = Path.Combine(_testRoot, "lock-timeout.txt");

        _output.WriteLine($"[{linkId}] Starting: lock timeout test");

        var result1 = await FileLockService.AcquireAsync(filePath, TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        result1.Success.Should().BeTrue("first lock should be acquired");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result2 = await FileLockService.AcquireAsync(filePath, TimeSpan.FromSeconds(2)).ConfigureAwait(true);
        sw.Stop();

        result2.Success.Should().BeFalse("second lock should fail due to timeout");
        sw.Elapsed.Should().BeGreaterThan(TimeSpan.FromSeconds(1),
            "should wait at least close to the timeout duration");

        await result1.Lock!.DisposeAsync().ConfigureAwait(true);

        var result3 = await FileLockService.AcquireAsync(filePath, TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        result3.Success.Should().BeTrue("lock should be available after release");
        await result3.Lock!.DisposeAsync().ConfigureAwait(true);

        _output.WriteLine($"[{linkId}] PASSED: Lock timeout works correctly, waited {sw.ElapsedMilliseconds}ms");
    }

    // ─────────────────────────────────────────────────────────────
    // LINK-007: HighWaterMarkManager 读-改-写原子性
    // 场景: 并发 Read + Update 操作，验证 Update 不丢失
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task LINK_007_HighWaterMarkManager_ReadUpdateAtomicity_ShouldNotLoseUpdates()
    {
        const string linkId = "LINK-007";
        var taskDir = Path.Combine(_testRoot, "hwm-atomic");
        _fs.CreateDirectory(taskDir);

        var options = TaskDirectoryOptionsBuilder.Create()
            .WithTaskDirectoryPath(taskDir)
            .Build();

        var manager = new HighWaterMarkManager(_fs, options);

        await manager.UpdateAsync(0).ConfigureAwait(true);

        const int updateCount = 10;
        var updateTasks = Enumerable.Range(1, updateCount).Select(i =>
            manager.UpdateAsync(i));

        await Task.WhenAll(updateTasks).ConfigureAwait(true);

        var finalValue = await manager.ReadAsync().ConfigureAwait(true);
        finalValue.Should().BeOneOf(Enumerable.Range(1, updateCount).ToArray(),
            "final value should be one of the update values, not lost");

        _output.WriteLine($"[{linkId}] Final value: {finalValue}");
        _output.WriteLine($"[{linkId}] PASSED: Read-Update atomicity verified");
    }
}
