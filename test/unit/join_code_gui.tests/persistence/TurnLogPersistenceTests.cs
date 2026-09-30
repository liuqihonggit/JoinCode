namespace JoinCode.Gui.Tests.Persistence;

/// <summary>
/// TurnLogPersistence 测试 — 验证轮次日志追加与撤回标记（任务8）。
/// 用移动代替删除：撤回不删除 turns.log，而是重命名为 .undo 后缀保留审计追踪。
/// </summary>
public class TurnLogPersistenceTests {
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task AppendTurn_CreatesLogWithTurnHeader() {
        await using var fs = new InMemoryFileSystem();
        var sessionsDir = fs.CombinePath("mem", "sessions");
        var persistence = new TurnLogPersistence(fs, sessionsDir);

        var entries = new List<TurnLogEntry> {
            new("user", "你好"),
            new("assistant", "你好！有什么可以帮你的？")
        };

        await persistence.AppendTurnAsync("sess-001", 0, entries);

        var logPath = fs.CombinePath(sessionsDir, "sess-001", "turns.log");
        fs.FileExists(logPath).Should().BeTrue();
        var content = await fs.ReadAllTextAsync(logPath);
        content.Should().Contain("[Turn 0]");
        content.Should().Contain("[user] 你好");
        content.Should().Contain("[assistant] 你好！有什么可以帮你的？");
        content.Should().Contain("---");
    }

    [Fact]
    public async Task AppendTurn_MultipleTurnsAccumulateInSameFile() {
        await using var fs = new InMemoryFileSystem();
        var sessionsDir = fs.CombinePath("mem", "sessions");
        var persistence = new TurnLogPersistence(fs, sessionsDir);

        await persistence.AppendTurnAsync("s1", 0, [new TurnLogEntry("user", "第一轮")]);
        await persistence.AppendTurnAsync("s1", 1, [new TurnLogEntry("user", "第二轮")]);

        var logPath = fs.CombinePath(sessionsDir, "s1", "turns.log");
        var content = await fs.ReadAllTextAsync(logPath);
        content.Should().Contain("[Turn 0]");
        content.Should().Contain("第一轮");
        content.Should().Contain("[Turn 1]");
        content.Should().Contain("第二轮");
    }

    [Fact]
    public async Task AppendTurn_EmptyEntries_DoesNothing() {
        await using var fs = new InMemoryFileSystem();
        var sessionsDir = fs.CombinePath("mem", "sessions");
        var persistence = new TurnLogPersistence(fs, sessionsDir);

        await persistence.AppendTurnAsync("s1", 0, []);

        var logPath = fs.CombinePath(sessionsDir, "s1", "turns.log");
        fs.FileExists(logPath).Should().BeFalse();
    }

    [Fact]
    public async Task AppendTurn_DifferentSessions_SeparateLogFiles() {
        await using var fs = new InMemoryFileSystem();
        var sessionsDir = fs.CombinePath("mem", "sessions");
        var persistence = new TurnLogPersistence(fs, sessionsDir);

        await persistence.AppendTurnAsync("s1", 0, [new TurnLogEntry("user", "会话1")]);
        await persistence.AppendTurnAsync("s2", 0, [new TurnLogEntry("user", "会话2")]);

        var log1 = await fs.ReadAllTextAsync(fs.CombinePath(sessionsDir, "s1", "turns.log"));
        var log2 = await fs.ReadAllTextAsync(fs.CombinePath(sessionsDir, "s2", "turns.log"));
        log1.Should().Contain("会话1").And.NotContain("会话2");
        log2.Should().Contain("会话2").And.NotContain("会话1");
    }

    [Fact]
    public async Task MarkUndo_RenamesLogToUndoSuffix() {
        await using var fs = new InMemoryFileSystem();
        var sessionsDir = fs.CombinePath("mem", "sessions");
        var persistence = new TurnLogPersistence(fs, sessionsDir);

        await persistence.AppendTurnAsync("s1", 0, [new TurnLogEntry("user", "测试撤回")]);
        var logPath = fs.CombinePath(sessionsDir, "s1", "turns.log");
        fs.FileExists(logPath).Should().BeTrue();

        await persistence.MarkUndoAsync("s1", 0);

        fs.FileExists(logPath).Should().BeFalse();
        var undoFiles = fs.GetFiles(fs.CombinePath(sessionsDir, "s1"), "*.undo", SearchOption.TopDirectoryOnly);
        undoFiles.Should().HaveCount(1);
        undoFiles[0].Should().Contain("turns_0_");
        undoFiles[0].Should().EndWith(".undo");
    }

    [Fact]
    public async Task MarkUndo_NoLogFile_IsNoOp() {
        await using var fs = new InMemoryFileSystem();
        var sessionsDir = fs.CombinePath("mem", "sessions");
        var persistence = new TurnLogPersistence(fs, sessionsDir);

        // 无 turns.log 时调用 MarkUndo 不抛异常
        await persistence.MarkUndoAsync("s1", 0);

        var sessionDir = fs.CombinePath(sessionsDir, "s1");
        if (fs.DirectoryExists(sessionDir)) {
            var files = fs.GetFiles(sessionDir, "*", SearchOption.TopDirectoryOnly);
            files.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task MarkUndo_ThenAppend_CreatesNewLog() {
        await using var fs = new InMemoryFileSystem();
        var sessionsDir = fs.CombinePath("mem", "sessions");
        var persistence = new TurnLogPersistence(fs, sessionsDir);

        await persistence.AppendTurnAsync("s1", 0, [new TurnLogEntry("user", "旧轮次")]);
        await persistence.MarkUndoAsync("s1", 0);

        // 撤回后新轮次应创建新的 turns.log
        await persistence.AppendTurnAsync("s1", 1, [new TurnLogEntry("user", "新轮次")]);

        var logPath = fs.CombinePath(sessionsDir, "s1", "turns.log");
        fs.FileExists(logPath).Should().BeTrue();
        var content = await fs.ReadAllTextAsync(logPath);
        content.Should().Contain("新轮次");
        content.Should().NotContain("旧轮次");

        // .undo 文件仍保留旧内容（审计追踪）
        var undoFiles = fs.GetFiles(fs.CombinePath(sessionsDir, "s1"), "*.undo", SearchOption.TopDirectoryOnly);
        undoFiles.Should().HaveCount(1);
        var undoContent = await fs.ReadAllTextAsync(undoFiles[0]);
        undoContent.Should().Contain("旧轮次");
    }

    [Fact]
    public async Task AppendTurn_CreatesDirectoryIfMissing() {
        await using var fs = new InMemoryFileSystem();
        var sessionsDir = fs.CombinePath("mem", "sessions");
        var persistence = new TurnLogPersistence(fs, sessionsDir);

        // 目录不存在时追加应自动创建
        await persistence.AppendTurnAsync("new-sess", 0, [new TurnLogEntry("user", "自动建目录")]);

        var logPath = fs.CombinePath(sessionsDir, "new-sess", "turns.log");
        fs.FileExists(logPath).Should().BeTrue();
    }

    // === 边缘场景 ===

    [Fact]
    public async Task MarkUndo_CalledTwice_SecondIsNoOp() {
        await using var fs = new InMemoryFileSystem();
        var sessionsDir = fs.CombinePath("mem", "sessions");
        var persistence = new TurnLogPersistence(fs, sessionsDir);

        await persistence.AppendTurnAsync("s1", 0, [new TurnLogEntry("user", "内容")]);
        await persistence.MarkUndoAsync("s1", 0);

        // 第二次撤回：turns.log 已不存在，应空操作不抛异常
        await persistence.MarkUndoAsync("s1", 0);

        var undoFiles = fs.GetFiles(fs.CombinePath(sessionsDir, "s1"), "*.undo", SearchOption.TopDirectoryOnly);
        undoFiles.Should().HaveCount(1);
    }

    [Fact]
    public async Task MarkUndo_OneSession_DoesNotAffectAnother() {
        await using var fs = new InMemoryFileSystem();
        var sessionsDir = fs.CombinePath("mem", "sessions");
        var persistence = new TurnLogPersistence(fs, sessionsDir);

        await persistence.AppendTurnAsync("s1", 0, [new TurnLogEntry("user", "会话1内容")]);
        await persistence.AppendTurnAsync("s2", 0, [new TurnLogEntry("user", "会话2内容")]);

        await persistence.MarkUndoAsync("s1", 0);

        // s1 被撤回，s2 的 turns.log 应不受影响
        var s2Log = fs.CombinePath(sessionsDir, "s2", "turns.log");
        fs.FileExists(s2Log).Should().BeTrue();
        var s2Content = await fs.ReadAllTextAsync(s2Log);
        s2Content.Should().Contain("会话2内容");

        // s1 的 turns.log 应已重命名
        var s1Log = fs.CombinePath(sessionsDir, "s1", "turns.log");
        fs.FileExists(s1Log).Should().BeFalse();
    }

    [Fact]
    public async Task AppendTurn_SpecialCharacters_PreservedInLog() {
        await using var fs = new InMemoryFileSystem();
        var sessionsDir = fs.CombinePath("mem", "sessions");
        var persistence = new TurnLogPersistence(fs, sessionsDir);

        var specialContent = "含换行\n和制表\t和括号[Turn 999]及中文【特殊】符号";
        await persistence.AppendTurnAsync("s1", 0, [new TurnLogEntry("user", specialContent)]);

        var logPath = fs.CombinePath(sessionsDir, "s1", "turns.log");
        var content = await fs.ReadAllTextAsync(logPath);
        content.Should().Contain("含换行");
        content.Should().Contain("和制表");
        content.Should().Contain("[Turn 999]");
        content.Should().Contain("【特殊】");
    }

    [Fact]
    public async Task AppendTurn_ManyTurns_AllAccumulateCorrectly() {
        await using var fs = new InMemoryFileSystem();
        var sessionsDir = fs.CombinePath("mem", "sessions");
        var persistence = new TurnLogPersistence(fs, sessionsDir);

        const int turnCount = 50;
        for (var i = 0; i < turnCount; i++)
            await persistence.AppendTurnAsync("s1", i, [new TurnLogEntry("user", $"第{i}轮")]);

        var logPath = fs.CombinePath(sessionsDir, "s1", "turns.log");
        var content = await fs.ReadAllTextAsync(logPath);
        for (var i = 0; i < turnCount; i++) {
            content.Should().Contain($"[Turn {i}]");
            content.Should().Contain($"第{i}轮");
        }
    }

    [Fact]
    public async Task AppendTurn_ConcurrentSameSession_AllAppended() {
        await using var fs = new InMemoryFileSystem();
        var sessionsDir = fs.CombinePath("mem", "sessions");
        var persistence = new TurnLogPersistence(fs, sessionsDir);

        var tasks = Enumerable.Range(0, 10)
            .Select(i => persistence.AppendTurnAsync("s1", i, [new TurnLogEntry("user", $"并发{i}")]))
            .ToArray();
        await Task.WhenAll(tasks);

        var logPath = fs.CombinePath(sessionsDir, "s1", "turns.log");
        fs.FileExists(logPath).Should().BeTrue();
        var content = await fs.ReadAllTextAsync(logPath);
        for (var i = 0; i < 10; i++)
            content.Should().Contain($"并发{i}");
    }

    [Fact]
    public async Task AppendTurn_SingleTurnManyEntries_AllWritten() {
        await using var fs = new InMemoryFileSystem();
        var sessionsDir = fs.CombinePath("mem", "sessions");
        var persistence = new TurnLogPersistence(fs, sessionsDir);

        var entries = Enumerable.Range(0, 20)
            .Select(i => new TurnLogEntry(i % 2 == 0 ? "user" : "assistant", $"消息{i}"))
            .ToList();

        await persistence.AppendTurnAsync("s1", 0, entries);

        var logPath = fs.CombinePath(sessionsDir, "s1", "turns.log");
        var content = await fs.ReadAllTextAsync(logPath);
        for (var i = 0; i < 20; i++)
            content.Should().Contain($"消息{i}");
    }

    [Fact]
    public async Task MarkUndo_ThenAppendMultipleTurns_OnlyNewTurnsInLog() {
        await using var fs = new InMemoryFileSystem();
        var sessionsDir = fs.CombinePath("mem", "sessions");
        var persistence = new TurnLogPersistence(fs, sessionsDir);

        await persistence.AppendTurnAsync("s1", 0, [new TurnLogEntry("user", "旧0")]);
        await persistence.AppendTurnAsync("s1", 1, [new TurnLogEntry("user", "旧1")]);
        await persistence.MarkUndoAsync("s1", 0);

        await persistence.AppendTurnAsync("s1", 2, [new TurnLogEntry("user", "新2")]);
        await persistence.AppendTurnAsync("s1", 3, [new TurnLogEntry("user", "新3")]);

        var logPath = fs.CombinePath(sessionsDir, "s1", "turns.log");
        var content = await fs.ReadAllTextAsync(logPath);
        content.Should().Contain("新2").And.Contain("新3");
        content.Should().NotContain("旧0").And.NotContain("旧1");

        // .undo 保留旧内容
        var undoFiles = fs.GetFiles(fs.CombinePath(sessionsDir, "s1"), "*.undo", SearchOption.TopDirectoryOnly);
        undoFiles.Should().HaveCount(1);
        var undoContent = await fs.ReadAllTextAsync(undoFiles[0]);
        undoContent.Should().Contain("旧0").And.Contain("旧1");
    }

    [Fact]
    public async Task AppendTurn_LargeTurnIndex_FormattedCorrectly() {
        await using var fs = new InMemoryFileSystem();
        var sessionsDir = fs.CombinePath("mem", "sessions");
        var persistence = new TurnLogPersistence(fs, sessionsDir);

        await persistence.AppendTurnAsync("s1", 99999, [new TurnLogEntry("user", "大轮次编号")]);

        var logPath = fs.CombinePath(sessionsDir, "s1", "turns.log");
        var content = await fs.ReadAllTextAsync(logPath);
        content.Should().Contain("[Turn 99999]");
    }

    [Fact]
    public async Task MarkUndo_NoSessionDirectory_DoesNotThrow() {
        await using var fs = new InMemoryFileSystem();
        var sessionsDir = fs.CombinePath("mem", "sessions");
        var persistence = new TurnLogPersistence(fs, sessionsDir);

        // 会话目录完全不存在时撤回不应抛异常
        var act = () => persistence.MarkUndoAsync("never-existed", 0);
        await act.Should().NotThrowAsync();
    }
}
