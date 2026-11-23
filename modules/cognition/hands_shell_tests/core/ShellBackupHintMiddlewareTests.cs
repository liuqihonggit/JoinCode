namespace Hands.Tests.Shell;

/// <summary>
/// ShellBackupHintMiddleware 单元测试 — 验证破坏性命令检测、备份提示注入、冷却去重（GAP-039-03）
/// </summary>
public class ShellBackupHintMiddlewareTests {

    // === IsDestructiveCommand ===

    [Theory]
    [InlineData("rm -rf /tmp/foo")]
    [InlineData("rm -r src/")]
    [InlineData("git reset --hard")]
    [InlineData("git clean -fd")]
    [InlineData("del /f /q file.txt")]
    [InlineData("rmdir /s /q build")]
    [InlineData("git commit -m test")]
    [InlineData("Move-Item old.cs new.cs -Force")]
    [InlineData("echo content > file.txt")]
    public void IsDestructiveCommand_DetectsDestructive(string command) {
        ShellBackupHintMiddleware.IsDestructiveCommand(command).Should().BeTrue();
    }

    [Theory]
    [InlineData("dotnet build")]
    [InlineData("git status")]
    [InlineData("git pull")]
    [InlineData("echo hello")]
    [InlineData("ls -la")]
    [InlineData("")]
    public void IsDestructiveCommand_DoesNotFlagSafeCommands(string command) {
        ShellBackupHintMiddleware.IsDestructiveCommand(command).Should().BeFalse();
    }

    // === BuildBackupHint ===

    [Fact]
    public void BuildBackupHint_ShouldContainGitStashSuggestion() {
        var hint = ShellBackupHintMiddleware.BuildBackupHint("rm -rf src/");
        hint.Should().Contain("git stash");
    }

    [Fact]
    public void BuildBackupHint_ShouldContainOriginalCommand() {
        var command = "git reset --hard origin/main";
        var hint = ShellBackupHintMiddleware.BuildBackupHint(command);
        hint.Should().Contain(command);
    }

    // === InvokeAsync ===

    [Fact]
    public async Task InvokeAsync_DestructiveCommand_ShouldInjectBackupReminder() {
        CooldownService.Reset();
        var fakeReminder = new FakeSystemReminderManager();
        await using var middleware = new ShellBackupHintMiddleware(fakeReminder);
        var context = MakeContext("rm -rf src/");
        var nextCalled = false;

        await middleware.InvokeAsync(context, (_, _) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        nextCalled.Should().BeTrue("中间件不应阻止管道继续");
        fakeReminder.AddedReminders.Should().HaveCount(1);
        fakeReminder.AddedReminders[0].id.Should().Be(ShellBackupHintMiddleware.ReminderIdConst);
    }

    [Fact]
    public async Task InvokeAsync_SafeCommand_ShouldNotInjectReminder() {
        CooldownService.Reset();
        var fakeReminder = new FakeSystemReminderManager();
        await using var middleware = new ShellBackupHintMiddleware(fakeReminder);
        var context = MakeContext("dotnet build");
        var nextCalled = false;

        await middleware.InvokeAsync(context, (_, _) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        nextCalled.Should().BeTrue();
        fakeReminder.AddedReminders.Should().BeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_CooldownPeriod_ShouldNotRepeatInjection() {
        CooldownService.Reset();
        var fakeReminder = new FakeSystemReminderManager();
        await using var middleware = new ShellBackupHintMiddleware(fakeReminder);

        await middleware.InvokeAsync(MakeContext("rm -rf a/"), (_, _) => Task.CompletedTask, CancellationToken.None);
        await middleware.InvokeAsync(MakeContext("rm -rf b/"), (_, _) => Task.CompletedTask, CancellationToken.None);

        fakeReminder.AddedReminders.Should().HaveCount(1, "冷却期内第二次不应重复注入");
    }

    [Fact]
    public async Task InvokeAsync_NullReminderManager_ShouldStillPassThrough() {
        CooldownService.Reset();
        await using var middleware = new ShellBackupHintMiddleware(reminderManager: null);
        var context = MakeContext("rm -rf src/");
        var nextCalled = false;

        await middleware.InvokeAsync(context, (_, _) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        nextCalled.Should().BeTrue("无 SystemReminderManager 时仍应放行管道");
    }

    private static ShellPipelineContext MakeContext(string command) {
        var providerMock = new Mock<ISystemActuator>();
        providerMock.SetupGet(x => x.Kind).Returns(SystemActuatorKind.Bash);
        return new ShellPipelineContext {
            Command = command,
            Provider = providerMock.Object,
        };
    }

    private sealed class FakeSystemReminderManager : ISystemReminderManager {
        public List<(string id, string content, int priority)> AddedReminders { get; } = new();

        public Task AddReminderAsync(string id, string content, int priority = 0, CancellationToken ct = default) {
            AddedReminders.Add((id, content, priority));
            return Task.CompletedTask;
        }

        public Task RemoveReminderAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<SystemReminder>> GetRemindersAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SystemReminder>>(Array.Empty<SystemReminder>());
        public Task ClearAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> FormatAsSystemRemindersAsync(CancellationToken ct = default) => Task.FromResult(string.Empty);
    }
}
