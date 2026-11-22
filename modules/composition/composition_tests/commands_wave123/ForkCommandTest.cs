namespace Composition.Tests.Commands;

/// <summary>
/// /fork 命令单元测试 — 覆盖转录服务缺失/无活跃会话/空转录/纯侧链消息四降级分支/
/// 主线消息复制到 fork 会话(ID 派生/IsSidechain 重置/字段保留/消息计数)/
/// 自定义标题覆盖/超长首提示截断/加载异常收敛
/// </summary>
[Collection("command-output")]
public sealed class ForkCommandTest {
    private static TranscriptEntry Entry(string role, string content, bool isSidechain = false) =>
        new() {
            SessionId = "parent-session",
            Role = role,
            Content = content,
            Timestamp = DateTime.UtcNow,
            IsSidechain = isSidechain,
        };

    private static (ForkCommand Command, ChatCommandContext Context, Mock<ITranscriptService> Transcript) Arrange(
        string arguments = "",
        string sessionId = "parent-session",
        Mock<ITranscriptService>? transcript = null) {
        var service = transcript ?? new Mock<ITranscriptService>();
        var services = CommandTestHarness.CreateServices(
            configureDi: di => di.AddSingleton(service.Object));
        var context = CommandTestHarness.CreateContext(services, arguments: arguments, sessionId: sessionId);
        return (new ForkCommand(), context, service);
    }

    [Fact]
    public async Task Execute_WithoutTranscriptService_ShowsError() {
        var services = CommandTestHarness.CreateServices();
        var context = CommandTestHarness.CreateContext(services);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new ForkCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("会话转录服务不可用");
    }

    [Fact]
    public async Task Execute_WithoutActiveSession_ShowsError() {
        var (cmd, context, transcript) = Arrange(sessionId: "");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("当前无活跃会话");
        transcript.Verify(t => t.LoadTranscriptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "无会话 ID 时不应加载转录");
    }

    [Fact]
    public async Task Execute_EmptyTranscript_ShowsError() {
        var transcript = new Mock<ITranscriptService>();
        transcript.Setup(t => t.LoadTranscriptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TranscriptEntry>());
        var (cmd, context, _) = Arrange(transcript: transcript);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("当前会话没有消息，无法创建分支");
    }

    [Fact]
    public async Task Execute_OnlySidechainEntries_ShowsError() {
        var transcript = new Mock<ITranscriptService>();
        transcript.Setup(t => t.LoadTranscriptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TranscriptEntry> {
                Entry("user", "side question", isSidechain: true),
                Entry("assistant", "side answer", isSidechain: true),
            });
        var (cmd, context, _) = Arrange(transcript: transcript);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("没有可分支的主对话消息", "纯侧链消息不应创建分支");
    }

    [Fact]
    public async Task Execute_MainlineMessages_CopiesToForkSession() {
        var transcript = new Mock<ITranscriptService>();
        transcript.Setup(t => t.LoadTranscriptAsync("parent-session", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TranscriptEntry> {
                Entry("user", "Hello World"),
                Entry("assistant", "Hi there"),
                Entry("user", "side question", isSidechain: true),
            });
        IReadOnlyList<TranscriptEntry>? forkedEntries = null;
        string? forkSessionId = null;
        transcript.Setup(t => t.AppendEntriesAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<TranscriptEntry>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<TranscriptEntry>, CancellationToken>((id, entries, _) => {
                forkSessionId = id;
                forkedEntries = entries;
            })
            .Returns(Task.CompletedTask);
        var (cmd, context, _) = Arrange(transcript: transcript);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        forkSessionId.Should().NotBeNull();
        forkSessionId.Should().StartWith("parent-session-fork-", "fork 会话 ID 应为 {parent}-fork-{序号} 派生格式");
        forkedEntries.Should().NotBeNull();
        Assert.NotNull(forkedEntries);
        forkedEntries.Count.Should().Be(2, "仅主对话消息(2条)应被复制,侧链消息(1条)应排除");
        forkedEntries.Should().OnlyContain(e => !e.IsSidechain, "复制的条目 IsSidechain 应重置为 false");
        forkedEntries[0].Role.Should().Be("user", "复制应保留消息角色");
        forkedEntries[0].Content.Should().Be("Hello World", "复制应保留消息内容");
        forkedEntries.Select(e => e.SessionId).Should().AllBe(forkSessionId,
            "复制后的条目 SessionId 应全部指向 fork 会话");
        output.Should().Contain("已创建对话分支");
        output.Should().Contain("消息数: 2");
        output.Should().Contain("Hello World (Branch)", "默认标题应从首条用户消息推导");
        output.Should().Contain("使用 /resume ", "应引导用户恢复到分支会话");
    }

    [Fact]
    public async Task Execute_CustomTitle_OverridesDerivedTitle() {
        var transcript = new Mock<ITranscriptService>();
        transcript.Setup(t => t.LoadTranscriptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TranscriptEntry> { Entry("user", "Hello World") });
        transcript.Setup(t => t.AppendEntriesAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<TranscriptEntry>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var (cmd, context, _) = Arrange(arguments: "my-branch", transcript: transcript);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("my-branch (Branch)", "自定义名称应覆盖从首条消息推导的标题");
        output.Should().NotContain("Hello World (Branch)");
    }

    [Fact]
    public async Task Execute_LongFirstPrompt_TruncatesTitleWithEllipsis() {
        var longPrompt = new string('x', 150);
        var transcript = new Mock<ITranscriptService>();
        transcript.Setup(t => t.LoadTranscriptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TranscriptEntry> { Entry("user", longPrompt) });
        transcript.Setup(t => t.AppendEntriesAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<TranscriptEntry>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var (cmd, context, _) = Arrange(transcript: transcript);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("...", "超长首提示(150字符)推导的标题应截断加省略号");
        output.Should().NotContain(new string('x', 150), "完整 150 字符不应出现在标题行");
    }

    [Fact]
    public async Task Execute_LoadFailure_ConvergesToHandledError() {
        var transcript = new Mock<ITranscriptService>();
        transcript.Setup(t => t.LoadTranscriptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var (cmd, context, _) = Arrange(transcript: transcript);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("转录加载异常应被收敛,不应中断会话循环");
        output.Should().Contain("创建分支失败: boom");
        transcript.Verify(t => t.AppendEntriesAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<TranscriptEntry>>(), It.IsAny<CancellationToken>()),
            Times.Never, "加载失败时不应写入任何 fork 条目");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new ForkCommand();
        cmd.Name.Should().Be("fork");
        cmd.Usage.Should().StartWith("/fork");
        cmd.Aliases.Should().Contain("branch", "branch 为 /fork 的别名");
    }
}
