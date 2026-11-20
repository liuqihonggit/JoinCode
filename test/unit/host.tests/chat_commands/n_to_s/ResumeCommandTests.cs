namespace Host.Tests.ChatCommands;

public sealed class ResumeCommandTests {
    [Fact]
    public void Name_Should_Be_resume() {
        var cmd = new ResumeCommand();
        cmd.Name.Should().Be("resume");
    }

    [Fact]
    public void Description_Should_Be_恢复之前的会话() {
        var cmd = new ResumeCommand();
        cmd.Description.Should().Be("恢复之前的会话");
    }

    [Fact]
    public void Usage_Should_Start_With_Slash() {
        var cmd = new ResumeCommand();
        cmd.Usage.Should().StartWith("/resume");
    }

    [Fact]
    public void IsHidden_Should_Be_False() {
        var cmd = new ResumeCommand();
        cmd.IsHidden.Should().BeFalse();
    }

    [Fact]
    public void Aliases_Should_Contain_continue() {
        var cmd = new ResumeCommand();
        cmd.Aliases.Should().Contain("continue");
    }

    [Fact]
    public void ArgumentHint_Should_Not_Be_Empty() {
        var cmd = new ResumeCommand();
        cmd.ArgumentHint.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Execute_NoArgs_Should_Return_Continue() {
        var cmd = new ResumeCommand();
        var context = new ChatCommandContext {
            Arguments = "",
            CancellationToken = CancellationToken.None,
            Services = new CommandServiceProvider(new CommandServices {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                FileSystem = TestFileSystem.Current,
            }),
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithSessionId_Should_Return_Continue() {
        var cmd = new ResumeCommand();
        var context = new ChatCommandContext {
            Arguments = "test-session",
            CancellationToken = CancellationToken.None,
            Services = new CommandServiceProvider(new CommandServices {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                FileSystem = TestFileSystem.Current,
            }),
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public void CrossProjectResumeResult_SameProject_Should_Not_Be_CrossProject() {
        var result = CrossProjectResumeResult.SameProject();
        result.IsCrossProject.Should().BeFalse();
    }

    [Fact]
    public void CrossProjectResumeResult_SameRepoWorktree_Should_Be_CrossProject_And_SameRepo() {
        var result = CrossProjectResumeResult.SameRepoWorktree("/path/to/worktree");
        result.IsCrossProject.Should().BeTrue();
        result.IsSameRepoWorktree.Should().BeTrue();
        result.ProjectPath.Should().Be("/path/to/worktree");
    }

    [Fact]
    public void CrossProjectResumeResult_DifferentProject_Should_Be_CrossProject_And_Not_SameRepo() {
        var result = CrossProjectResumeResult.DifferentProject("/path/to/other/project");
        result.IsCrossProject.Should().BeTrue();
        result.IsSameRepoWorktree.Should().BeFalse();
        result.ProjectPath.Should().Be("/path/to/other/project");
    }

    // ─── TASK069-C1 列表项增强：标题与元数据行（对齐 TS formatLogMetadata） ───

    [Fact]
    public void BuildSessionMetadata_Should_JoinTimeBranchAndMessageCount() {
        var lite = new SessionLiteData {
            Id = "abc12345",
            LastModified = DateTime.Now.AddMinutes(-3),
            BranchName = "main",
            MessageCount = 12,
        };

        ResumeCommand.BuildSessionMetadata(lite).Should().Be("3 分钟前 · main · 12 条消息");
    }

    [Fact]
    public void BuildSessionMetadata_Should_OmitBranchSegment_WhenBranchEmpty() {
        // 分支为空 → 整段省略（不留 " ·  · " 空档）
        var lite = new SessionLiteData {
            Id = "abc12345",
            LastModified = DateTime.Now.AddHours(-2),
            MessageCount = 7,
        };

        ResumeCommand.BuildSessionMetadata(lite).Should().Be("2 小时前 · 7 条消息");
    }

    [Fact]
    public void BuildSessionMetadata_Should_Always_ReportMessageCount_EvenZero() {
        var lite = new SessionLiteData {
            Id = "abc12345",
            LastModified = DateTime.Now,
            MessageCount = 0,
        };

        ResumeCommand.BuildSessionMetadata(lite).Should().Be("刚刚 · 0 条消息");
    }

    [Fact]
    public void BuildSessionPreview_Should_PreferCustomTitle() {
        var lite = new SessionLiteData { Id = "abcdefghijkl", CustomTitle = "修登录跳转" };

        ResumeCommand.BuildSessionPreview(lite).Should().Be("修登录跳转");
    }

    [Fact]
    public void BuildSessionPreview_Should_FallBackToTruncatedId_WhenTitleMissing() {
        // 基准 getLogDisplayTitle 兜底 sessionId.slice(0,8)；本仓库保持既有「+...」后缀
        var lite = new SessionLiteData { Id = "abcdefghijkl", CustomTitle = "" };

        ResumeCommand.BuildSessionPreview(lite).Should().Be("abcdefgh...");
    }

    // ─── TASK069-C3 交互冲突：TUI 宿主注入选择器时必须走原生对话框，不得落到 CLI Selector ───

    [Fact]
    public async Task ListSessions_Should_RouteToInjectedPicker_WithTitleAndMetadata() {
        var now = DateTime.Now;
        var transcript = new Mock<ITranscriptService>();
        transcript
            .Setup(t => t.ListTranscriptsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new TranscriptSummary {
                    SessionId = "s1",
                    LastModifiedAt = now.AddMinutes(-5),
                    MessageCount = 12,
                },
            ]);
        transcript.Setup(t => t.GetCustomTitleAsync("s1", It.IsAny<CancellationToken>())).ReturnsAsync("修登录跳转");
        transcript.Setup(t => t.GetSessionInfoAsync("s1", It.IsAny<CancellationToken>())).ReturnsAsync(new SessionInfo {
            Id = "s1",
            ProjectPath = Environment.CurrentDirectory,
            BranchName = "main",
        });

        var fallback = new Mock<IServiceProvider>();
        fallback.Setup(p => p.GetService(typeof(ITranscriptService))).Returns(transcript.Object);

        SessionPickerRequest? captured = null;
        var context = new ChatCommandContext {
            Arguments = string.Empty,
            SessionId = "current-session",
            CancellationToken = CancellationToken.None,
            Services = new CommandServiceProvider(
                new CommandServices {
                    ChatService = Mock.Of<IChatService>(),
                    CodeService = Mock.Of<ICodeService>(),
                    PlanService = Mock.Of<IPlanService>(),
                    FileSystem = TestFileSystem.Current,
                },
                fallback.Object),
            BeginSessionPicker = (request, _) => {
                captured = request;
                return Task.FromResult<string?>(null);
            },
        };

        await ResumeCommand.ListSessionsAsync(context, showAllProjects: false).ConfigureAwait(true);

        // 注入选择器即接管 —— Selector 分支（WriteLineReal 直写）不会被走到
        captured.Should().NotBeNull();
        if (captured is null) {
            return;
        }

        captured.Entries.Should().HaveCount(1);
        captured.Entries[0].SessionId.Should().Be("s1");
        captured.Entries[0].Preview.Should().Be("修登录跳转");
        captured.Entries[0].Metadata.Should().Contain("main").And.Contain("12 条消息");
    }
}