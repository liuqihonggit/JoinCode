namespace NativeTui.Tests.Repl.CommandsUx;

/// <summary>
/// CommandUsageMemory 持久化测试 — JSON 文件往返（DTO + JsonContext 双向转换）、
/// 坏文件静默降级空记忆、文件不存在降级、超容量裁剪、原子覆盖写、父目录自动创建。
/// 临时目录隔离（用例自建自清）。
/// </summary>
public sealed class CommandUsagePersistenceTests
{
    /// <summary>创建隔离临时目录（自动创建 + 用例结束统一清理）。</summary>
    private static string TempDir()
    {
        var directory = Path.Combine(Path.GetTempPath(), "nativetui-usage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>保存后加载：计数/时间戳/名字完整往返。</summary>
    [Fact]
    public async Task SaveThenLoad_RoundTripsEntries()
    {
        var directory = TempDir();
        try
        {
            var path = Path.Combine(directory, "command-history.json");
            var time = new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero));
            var memory = new CommandUsageMemory(timeProvider: time);
            memory.RecordUse("compact");
            memory.RecordUse("compact");
            memory.RecordUse("theme");

            await memory.SaveAsync(path);

            var loaded = await CommandUsageMemory.LoadAsync(path);
            Assert.Equal(2, loaded.Count);
            var compact = loaded.TryGetUsage("compact");
            Assert.NotNull(compact);
            Assert.Equal(2, compact.Count);
            var theme = loaded.TryGetUsage("theme");
            Assert.NotNull(theme);
            Assert.Equal(1, theme.Count);
            Assert.Equal(new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero), theme.LastUsed);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>文件不存在：降级空记忆（历史不阻塞启动）。</summary>
    [Fact]
    public async Task Load_MissingFile_ReturnsEmptyMemory()
    {
        var directory = TempDir();
        try
        {
            var memory = await CommandUsageMemory.LoadAsync(Path.Combine(directory, "absent.json"));
            Assert.Equal(0, memory.Count);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>坏 JSON：静默降级空记忆（损坏历史不崩溃）。</summary>
    [Fact]
    public async Task Load_CorruptJson_DegradesToEmptyMemory()
    {
        var directory = TempDir();
        try
        {
            var path = Path.Combine(directory, "command-history.json");
            await File.WriteAllTextAsync(path, "not-json{{{");

            var memory = await CommandUsageMemory.LoadAsync(path);
            Assert.Equal(0, memory.Count);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>空 entries 数组：空记忆（合法空文件）。</summary>
    [Fact]
    public async Task Load_EmptyEntriesArray_ReturnsEmptyMemory()
    {
        var directory = TempDir();
        try
        {
            var path = Path.Combine(directory, "command-history.json");
            await File.WriteAllTextAsync(path, """{"entries":[]}""");

            var memory = await CommandUsageMemory.LoadAsync(path);
            Assert.Equal(0, memory.Count);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>超容量文件：加载后裁剪至容量（按最近使用时间保留最新）。</summary>
    [Fact]
    public async Task Load_TrimsBeyondCapacity()
    {
        var directory = TempDir();
        try
        {
            var path = Path.Combine(directory, "command-history.json");
            var baseTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var entries = Enumerable.Range(1, 5)
                .Select(i => $"{{\"name\":\"cmd{i}\",\"count\":1,\"lastUsed\":\"{baseTime.AddMinutes(i):O}\"}}")
                .ToList();
            await File.WriteAllTextAsync(path, $$"""{"entries":[{{string.Join(",", entries)}}]}""");

            var memory = await CommandUsageMemory.LoadAsync(path, capacity: 3);
            Assert.Equal(3, memory.Count);
            Assert.Null(memory.TryGetUsage("cmd1"));
            Assert.Null(memory.TryGetUsage("cmd2"));
            Assert.NotNull(memory.TryGetUsage("cmd3"));
            Assert.NotNull(memory.TryGetUsage("cmd4"));
            Assert.NotNull(memory.TryGetUsage("cmd5"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>重复保存：原子覆盖旧文件（不留 .tmp 残留）。</summary>
    [Fact]
    public async Task Save_OverwritesExistingFileWithoutTempResidue()
    {
        var directory = TempDir();
        try
        {
            var path = Path.Combine(directory, "command-history.json");
            var memory = new CommandUsageMemory();
            memory.RecordUse("compact");
            await memory.SaveAsync(path);
            memory.RecordUse("theme");
            await memory.SaveAsync(path);

            var loaded = await CommandUsageMemory.LoadAsync(path);
            Assert.Equal(2, loaded.Count);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>父目录不存在：保存时自动创建。</summary>
    [Fact]
    public async Task Save_CreatesParentDirectory()
    {
        var directory = TempDir();
        try
        {
            var path = Path.Combine(directory, "nested", "deep", "command-history.json");
            var memory = new CommandUsageMemory();
            memory.RecordUse("compact");

            await memory.SaveAsync(path);

            Assert.True(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>默认持久化路径：~/.jcc/command-history.json（配置目录先例对齐）。</summary>
    [Fact]
    public void DefaultFilePath_UnderUserJccDirectory()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jcc", "command-history.json");
        Assert.Equal(expected, CommandUsageMemory.DefaultFilePath);
    }
}
