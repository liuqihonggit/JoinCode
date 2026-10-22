namespace Composition.Tests.Commands;

/// <summary>
/// /diag 命令单元测试 — 覆盖存储未注册降级/空参 recent(空报告+有快照报告)/
/// fence 按围栏查询(有/无记录)/ack(非法 ID/不存在/成功确认)/detail(非法 ID/成功详情)/未知操作用法
/// <para>ICrashSnapshotStore 用真实 CrashSnapshotStore 实例经 DI 注入
/// (recent 分支强转具体类,不能用 Moq);快照经 Add 构造真实数据</para>
/// </summary>
[Collection("command-output")]
public sealed class DiagCommandTest {
    private static CrashSnapshot CreateSnapshot(
        string fence = "fence-main",
        CrashSeverity severity = CrashSeverity.Error,
        string message = "计划执行失败") =>
        new(fence, severity, new InvalidOperationException(message));

    private static (DiagCommand Command, CrashSnapshotStore Store, ChatCommandContext Context) Arrange(
        string arguments = "",
        bool withStore = true,
        params CrashSnapshot[] snapshots) {
        var store = new CrashSnapshotStore();
        foreach (var snapshot in snapshots) {
            store.Add(snapshot);
        }
        var services = CommandTestHarness.CreateServices(
            configureDi: di => {
                if (withStore) di.AddSingleton<ICrashSnapshotStore>(store);
            });
        var context = CommandTestHarness.CreateContext(services, arguments: arguments);
        return (new DiagCommand(), store, context);
    }

    [Fact]
    public async Task Execute_WithoutStore_ShowsUnavailable() {
        var (cmd, store, context) = Arrange(withStore: false);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        output.Should().Contain("CrashSnapshotStore 未初始化 — 诊断功能不可用");
    }

    [Fact]
    public async Task Execute_EmptyArgs_EmptyStore_ShowsEmptyReport() {
        var (cmd, _, context) = Arrange(arguments: "");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("无崩溃快照记录。");
    }

    [Fact]
    public async Task Execute_EmptyArgs_WithSnapshots_ShowsRecentReport() {
        var snapshot = CreateSnapshot(message: "严重故障");
        var (cmd, _, context) = Arrange(arguments: "", snapshots: [snapshot]);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("崩溃快照报告（最近 1 条，共 1 条，未确认 1 条）");
        output.Should().Contain("[ERROR] fence-main");
        output.Should().Contain("严重故障");
        output.Should().Contain($"ID: {snapshot.Id:N}");
    }

    [Fact]
    public async Task Execute_RecentArg_SameAsDefault() {
        var (cmd, _, context) = Arrange(arguments: "recent", snapshots: [CreateSnapshot()]);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("崩溃快照报告（最近 1 条");
    }

    [Fact]
    public async Task Execute_Fence_WithRecords_ShowsFilteredSnapshots() {
        var (cmd, _, context) = Arrange(
            arguments: "fence fence-alpha",
            snapshots: [
                CreateSnapshot(fence: "fence-alpha", message: "A 崩溃"),
                CreateSnapshot(fence: "fence-beta", message: "B 崩溃"),
            ]);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("围栏 'fence-alpha' 崩溃记录 (1 条):");
        output.Should().Contain("A 崩溃");
        output.Should().NotContain("B 崩溃", "fence 过滤不应显示其他围栏的快照");
    }

    [Fact]
    public async Task Execute_Fence_NoRecords_ShowsEmptyHint() {
        var (cmd, _, context) = Arrange(arguments: "fence fence-nope", snapshots: [CreateSnapshot(fence: "fence-other")]);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("围栏 'fence-nope' 无崩溃记录");
    }

    [Fact]
    public async Task Execute_Ack_InvalidGuid_ShowsInvalidId() {
        var (cmd, _, context) = Arrange(arguments: "ack not-a-guid");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("无效的快照 ID: not-a-guid");
    }

    [Fact]
    public async Task Execute_Ack_UnknownGuid_ShowsNotFound() {
        var (cmd, _, context) = Arrange(arguments: "ack 00000000-0000-0000-0000-000000000000");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("不存在");
    }

    [Fact]
    public async Task Execute_Ack_ExistingSnapshot_AcknowledgesIt() {
        var snapshot = CreateSnapshot();
        var (cmd, store, context) = Arrange(arguments: $"ack {snapshot.Id}", snapshots: [snapshot]);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain($"快照 {snapshot.Id:N} 已确认");
        store.UnacknowledgedCount.Should().Be(0, "确认后未确认计数应归零");
    }

    [Fact]
    public async Task Execute_Detail_InvalidGuid_ShowsInvalidId() {
        var (cmd, _, context) = Arrange(arguments: "detail abc");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("无效的快照 ID: abc");
    }

    [Fact]
    public async Task Execute_Detail_ExistingSnapshot_ShowsFullDetail() {
        var snapshot = CreateSnapshot(fence: "fence-detail", message: "详情异常消息");
        var (cmd, _, context) = Arrange(arguments: $"detail {snapshot.Id}", snapshots: [snapshot]);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("═══ 崩溃快照");
        output.Should().Contain("围栏:   fence-detail");
        output.Should().Contain("严重度: ERROR");
        output.Should().Contain("详情异常消息");
        output.Should().Contain("异常类型: System.InvalidOperationException");
    }

    [Fact]
    public async Task Execute_UnknownAction_ShowsUsage() {
        var (cmd, _, context) = Arrange(arguments: "bogus");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("用法: /diag [recent|fence <name>|ack <id>|detail <id>]");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new DiagCommand();
        cmd.Name.Should().Be("diag");
        cmd.Usage.Should().Be("/diag [recent|fence <name>|ack <id>|clear]");
        cmd.IsHidden.Should().BeTrue();
    }
}
