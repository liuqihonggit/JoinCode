namespace Guard.Security.Tests;

/// <summary>
/// CommandExecutionAuditor 审计日志测试 — 验证 JSONL 写入和结构化记录
/// </summary>
public class CommandExecutionAuditorTests
{
    private static CommandExecutionAuditor CreateAuditor(InMemoryFileSystem fs, string dir = ".audit-test")
        => new(fs, dir, NullLogger<CommandExecutionAuditor>.Instance);

    [Fact]
    public void Record_Creates_Audit_Directory_And_File()
    {
        var fs = new InMemoryFileSystem();
        var auditor = CreateAuditor(fs);

        auditor.Record(new CommandExecutionAuditEntry(
            DateTimeOffset.Parse("2026-09-13T12:00:00Z"),
            "git status",
            CommandDangerLevel.Safe,
            PermissionMode.Unattended,
            "AutoExecuted"));

        fs.DirectoryExists(".audit-test").Should().BeTrue();
        fs.FileExists(".audit-test/command-execution-2026-09-13.jsonl").Should().BeTrue();
    }

    [Fact]
    public void Record_Writes_Valid_JSONL()
    {
        var fs = new InMemoryFileSystem();
        var auditor = CreateAuditor(fs);

        auditor.Record(new CommandExecutionAuditEntry(
            DateTimeOffset.Parse("2026-09-13T12:00:00Z"),
            "rm -rf /tmp",
            CommandDangerLevel.Execution,
            PermissionMode.Unattended,
            "AutoExecuted",
            "Recursive deletion"));

        var content = fs.ReadAllText(".audit-test/command-execution-2026-09-13.jsonl");
        content.Should().Contain("rm -rf /tmp");
        content.Should().Contain("Execution");
        content.Should().Contain("Unattended");
        content.Should().Contain("AutoExecuted");
        content.Should().Contain("Recursive deletion");
    }

    [Fact]
    public void Record_Appends_Multiple_Entries_To_Same_File()
    {
        var fs = new InMemoryFileSystem();
        var auditor = CreateAuditor(fs);
        var timestamp = DateTimeOffset.Parse("2026-09-13T12:00:00Z");

        auditor.Record(new CommandExecutionAuditEntry(timestamp, "cmd1", CommandDangerLevel.Safe, PermissionMode.Unattended, "AutoExecuted"));
        auditor.Record(new CommandExecutionAuditEntry(timestamp, "cmd2", CommandDangerLevel.Execution, PermissionMode.Unattended, "AutoExecuted"));

        var content = fs.ReadAllText(".audit-test/command-execution-2026-09-13.jsonl");
        content.Should().Contain("cmd1");
        content.Should().Contain("cmd2");
    }

    [Fact]
    public void Record_With_FilesChanged_Serializes_Changes()
    {
        var fs = new InMemoryFileSystem();
        var auditor = CreateAuditor(fs);

        var changes = new List<FileChangeRecord>
        {
            new("src/file.cs", FileChangeType.Modified, 100, 200),
            new("src/new.txt", FileChangeType.Created, null, 50),
            new("src/old.txt", FileChangeType.Deleted, 80, null),
        };

        auditor.Record(new CommandExecutionAuditEntry(
            DateTimeOffset.Parse("2026-09-13T12:00:00Z"),
            "git checkout .",
            CommandDangerLevel.Execution,
            PermissionMode.Unattended,
            "AutoExecuted",
            "File restoration",
            changes));

        var content = fs.ReadAllText(".audit-test/command-execution-2026-09-13.jsonl");
        content.Should().Contain("src/file.cs");
        content.Should().Contain("Modified");
        content.Should().Contain("src/new.txt");
        content.Should().Contain("Created");
        content.Should().Contain("src/old.txt");
        content.Should().Contain("Deleted");
    }

    [Fact]
    public void Record_Does_Not_Throw_On_FileSystem_Error()
    {
        var fs = new InMemoryFileSystem();
        var auditor = CreateAuditor(fs, "/nonexistent/path/that/should/not/exist");

        var act = () => auditor.Record(new CommandExecutionAuditEntry(
            DateTimeOffset.UtcNow,
            "test",
            CommandDangerLevel.Safe,
            PermissionMode.Unattended,
            "AutoExecuted"));

        act.Should().NotThrow();
    }
}
