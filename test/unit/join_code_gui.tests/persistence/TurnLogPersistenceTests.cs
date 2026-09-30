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
}
