namespace JoinCode.Entry.Tests;


/// <summary>
/// SessionResumeStep 单元测试 — 验证视角1 #1 的 --continue/--resume 行为
/// </summary>
public class SessionResumeStepTests {
    private static readonly string SessionsDir = AppDataConstants.Paths.SessionsDirectory;

    private static StartupContext CreateContext(CommandLineOptions options, IFileSystem fs) {
        var config = new WorkflowConfig {
            Provider = new ProviderConfig {
                ApiKey = "sk-test",
                Vendor = "openai",
                ModelId = "gpt-4o"
            }
        };

        var host = new Mock<IHost>().Object;
        return new StartupContext {
            Config = config,
            Options = options,
            Host = host,
            FileSystem = fs
        };
    }

    private static string WriteSessionFile(IFileSystem fs, string sessionId, string customTitle, DateTime lastModified, List<SessionMessage> messages) {
        var data = new SessionData {
            Id = sessionId,
            ProjectPath = "/test",
            CustomTitle = customTitle,
            CreatedAt = DateTime.UtcNow,
            Messages = messages
        };

        var json = JsonSerializer.Serialize(data, CliIndentedJsonContext.Default.SessionData);
        var path = Path.Combine(SessionsDir, $"{sessionId}.json");
        fs.WriteAllText(path, json).GetAwaiter().GetResult();
        return path;
    }

    [Fact]
    public async Task NoContinueAndNoResume_ShouldCallNextWithoutResume() {
        // Arrange — 默认 CommandLineOptions 无 --continue 也无 --resume
        await using var step = new SessionResumeStep();
        await using var fs = new InMemoryFileSystem();
        var context = CreateContext(new CommandLineOptions(), fs);
        var nextCalled = false;

        // Act
        await step.InvokeAsync(context, (_, _) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        // Assert — 不恢复时应直接放行
        nextCalled.Should().BeTrue("无 --continue 和 --resume 时应直接调用 next");
    }

    [Fact]
    public async Task Continue_WithNoSessionsDirectory_ShouldCallNextWithoutError() {
        // Arrange — sessions 目录不存在
        await using var step = new SessionResumeStep();
        await using var fs = new InMemoryFileSystem();
        var options = new CommandLineOptions { ContinueSession = true };
        var context = CreateContext(options, fs);
        var nextCalled = false;

        // Act
        await step.InvokeAsync(context, (_, _) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        // Assert — 目录不存在不应阻塞启动
        nextCalled.Should().BeTrue("无历史会话时应继续启动");
        context.ExitCode.Should().Be(0, "无会话可恢复不应设置错误退出码");
    }

    [Fact]
    public async Task Continue_WithSessions_ShouldLoadMostRecent() {
        // Arrange — 写入两个会话，一个旧一个新
        await using var step = new SessionResumeStep();
        await using var fs = new InMemoryFileSystem();

        // 创建 sessions 目录
        fs.CreateDirectory(SessionsDir);

        // 旧会话
        WriteSessionFile(fs, "old-session-id", "旧会话", DateTime.UtcNow.AddHours(-2),
            [new SessionMessage { Role = "user", Content = "旧消息" }]);

        // 新会话（最近）
        var newSessionPath = WriteSessionFile(fs, "new-session-id", "新会话", DateTime.UtcNow,
            [new SessionMessage { Role = "user", Content = "新消息 1" },
             new SessionMessage { Role = "assistant", Content = "新消息 2" }]);

        // --continue 应自动选择最近会话
        var options = new CommandLineOptions { ContinueSession = true };
        var context = CreateContext(options, fs);

        var nextCalled = false;
        await step.InvokeAsync(context, (_, _) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        // Assert
        nextCalled.Should().BeTrue("--continue 找到会话后应继续调用 next");
    }

    [Fact]
    public async Task Resume_WithExactSessionId_ShouldLoadSession() {
        // Arrange
        await using var step = new SessionResumeStep();
        await using var fs = new InMemoryFileSystem();
        fs.CreateDirectory(SessionsDir);

        WriteSessionFile(fs, "abc-123", "测试会话", DateTime.UtcNow,
            [new SessionMessage { Role = "user", Content = "hello" }]);

        // --resume abc-123
        var options = new CommandLineOptions { ResumeSessionId = "abc-123" };
        var context = CreateContext(options, fs);

        var nextCalled = false;
        await step.InvokeAsync(context, (_, _) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        // Assert
        nextCalled.Should().BeTrue("--resume 精确匹配到 sessionId 后应继续调用 next");
    }

    [Fact]
    public async Task Resume_WithNonExistentId_ShouldStillCallNextWithoutError() {
        // Arrange — Session 为 null（模拟 SessionInitStep 未初始化），应跳过恢复不阻塞
        await using var step = new SessionResumeStep();
        await using var fs = new InMemoryFileSystem();
        fs.CreateDirectory(SessionsDir);

        var options = new CommandLineOptions { ResumeSessionId = "non-existent-id" };
        var context = CreateContext(options, fs);

        var nextCalled = false;
        await step.InvokeAsync(context, (_, _) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        // Assert — Session 未初始化时应跳过恢复并继续（不进入 sessionId 查找路径）
        nextCalled.Should().BeTrue("Session 为 null 时应跳过恢复并继续");
    }

    /// <summary>
    /// Bug 11 回归: --resume <invalid-id> 当 Session 已初始化但会话不存在时，
    /// 应错误退出（ExitCode.SessionResumeFailed=5）而非静默创建新会话。
    /// 修复前：打印 hint 到 stdout 后继续启动新会话（污染 JSON 输出 + 误导用户）。
    /// 修复后：设置 ExitCode=5，输出结构化错误，不调用 next。
    /// </summary>
    [Fact]
    public async Task Resume_WithNonExistentId_AndSessionInitialized_ShouldFailWithError() {
        // Arrange — 需要完整的 Host 设置（ITranscriptService 返回会话不存在）
        await using var step = new SessionResumeStep();
        await using var fs = new InMemoryFileSystem();
        fs.CreateDirectory(SessionsDir);

        var transcriptMock = new Mock<ITranscriptService>();
        transcriptMock.Setup(t => t.TranscriptExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        transcriptMock.Setup(t => t.ListTranscriptsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(transcriptMock.Object);
        var serviceProvider = serviceCollection.BuildServiceProvider();
        var hostMock = new Mock<IHost>();
        hostMock.Setup(h => h.Services).Returns(serviceProvider);

        var config = new WorkflowConfig {
            Provider = new ProviderConfig { ApiKey = "sk-test", Vendor = "openai", ModelId = "gpt-4o" }
        };
        var context = new StartupContext {
            Config = config,
            Options = new CommandLineOptions { ResumeSessionId = "definitely-non-existent" },
            Host = hostMock.Object,
            FileSystem = fs,
            Session = new CliSession(
                new Mock<IChatService>().Object,
                new Mock<ICodeService>().Object,
                new Mock<IPlanService>().Object,
                new Mock<IToolRegistry>().Object,
                fs)
        };

        var nextCalled = false;
        await step.InvokeAsync(context, (_, _) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        // Assert — 未找到会话应错误退出，不继续对话
        nextCalled.Should().BeFalse("未找到 --resume 指定的会话时不应继续对话");
        context.ExitCode.Should().Be((int)ExitCode.SessionResumeFailed, "应设置 SessionResumeFailed 退出码");
    }

    [Fact]
    public async Task Resume_WithTitleMatch_ShouldLoadSession() {
        // Arrange — 通过标题模糊匹配
        await using var step = new SessionResumeStep();
        await using var fs = new InMemoryFileSystem();
        fs.CreateDirectory(SessionsDir);

        WriteSessionFile(fs, "session-001", "重构认证模块", DateTime.UtcNow,
            [new SessionMessage { Role = "user", Content = "开始重构" }]);

        var options = new CommandLineOptions { ResumeSessionId = "重构" };
        var context = CreateContext(options, fs);

        var nextCalled = false;
        await step.InvokeAsync(context, (_, _) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        // Assert
        nextCalled.Should().BeTrue("--resume 按标题匹配成功后应继续调用 next");
    }

    [Fact]
    public async Task Continue_WithoutSession_ShouldSkipResumeAndContinue() {
        // Arrange — Session 属性为 null（模拟 SessionInitStep 未初始化）
        await using var step = new SessionResumeStep();
        await using var fs = new InMemoryFileSystem();
        fs.CreateDirectory(SessionsDir);

        // Session = null（默认就是 null）
        var options = new CommandLineOptions { ContinueSession = true };
        var context = CreateContext(options, fs);
        // 不设置 context.Session，保持为 null

        var nextCalled = false;
        await step.InvokeAsync(context, (_, _) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        // Assert — Session 未初始化时不应阻塞
        nextCalled.Should().BeTrue("Session 为 null 时应跳过恢复并继续");
    }

    // ─── TASK070 B-6 — --continue 项目过滤（FindMostRecentSessionIdAsync internal 测试面）───
    // 路径一律跨平台构造（Path.GetTempPath 基座 + Path.Combine）— 禁止硬编码 Windows 盘符字面量：
    // Linux CI 的 Path.GetFullPath 不识别盘符/反斜杠，归一化语义分叉会破坏匹配（JCC1018 防复发规则）。

    /// <summary>构造跨平台项目路径（基座 + 段落 Path.Combine 拼接）。</summary>
    private static string ProjectPath(params string[] segments) =>
        Path.Combine([Path.GetTempPath(), "jcc-resume-test", .. segments]);

    /// <summary>同上但尾部追加平台分隔符（测归一容差）。</summary>
    private static string ProjectPathWithTrailingSep(params string[] segments) =>
        ProjectPath(segments) + Path.DirectorySeparatorChar;

    private static Mock<ITranscriptService> CreateTranscriptService((string SessionId, string? ProjectPath, DateTime Modified, int MessageCount)[] sessions) {
        var mock = new Mock<ITranscriptService>();
        mock.Setup(s => s.ListTranscriptsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessions
                .OrderByDescending(s => s.Modified)
                .Select(s => new TranscriptSummary {
                    SessionId = s.SessionId,
                    CreatedAt = s.Modified,
                    LastModifiedAt = s.Modified,
                    MessageCount = s.MessageCount,
                })
                .ToList());
        foreach (var (sessionId, projectPath, _, _) in sessions) {
            var info = projectPath is null ? null : new SessionInfo { Id = sessionId, ProjectPath = projectPath };
            mock.Setup(s => s.GetSessionInfoAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(info);
        }
        return mock;
    }

    [Fact]
    public async Task Continue_FiltersByProjectPath_MostRecentInCurrentProjectWins() {
        // Arrange — 最新会话属其他项目，较旧会话属当前项目（尾分隔符差异）→ 应选当前项目的
        var currentProject = ProjectPath("cur", "repo");
        var service = CreateTranscriptService([
            ("newest-other", ProjectPath("other", "repo"), DateTime.UtcNow.AddMinutes(-1), 1),
            ("older-current", ProjectPathWithTrailingSep("cur", "repo"), DateTime.UtcNow.AddHours(-2), 1),
        ]);

        var sessionId = await SessionResumeStep.FindMostRecentSessionIdAsync(service.Object, currentProject, CancellationToken.None);

        sessionId.Should().Be("older-current", "--continue 应按 ProjectPath 过滤选当前项目的最近会话（尾分隔符差异归一）");
    }

    [Fact]
    public async Task Continue_NoSessionInCurrentProject_ReturnsNull() {
        var service = CreateTranscriptService([
            ("other-1", ProjectPath("other", "repo"), DateTime.UtcNow.AddMinutes(-1), 1),
            ("other-2", ProjectPath("elsewhere", "root"), DateTime.UtcNow.AddHours(-2), 1),
        ]);

        var sessionId = await SessionResumeStep.FindMostRecentSessionIdAsync(
            service.Object, ProjectPath("cur", "repo"), CancellationToken.None);

        sessionId.Should().BeNull("无当前项目会话时应返回 null（上层提示并启动新会话）");
    }

    [Fact]
    public async Task Continue_SkipsLegacySessionsWithoutMeta_MatchesOlderCurrentProject() {
        // Arrange — 最新会话无 meta（旧存量），较旧的当前项目会话应命中
        var currentProject = ProjectPath("cur", "repo");
        var service = CreateTranscriptService([
            ("legacy-no-meta", null, DateTime.UtcNow.AddMinutes(-1), 1),
            ("current-project", currentProject, DateTime.UtcNow.AddHours(-2), 1),
        ]);

        var sessionId = await SessionResumeStep.FindMostRecentSessionIdAsync(service.Object, currentProject, CancellationToken.None);

        sessionId.Should().Be("current-project", "无 meta 的旧会话应跳过，不阻塞当前项目会话命中");
    }

    /// <summary>--continue 应跳过空会话（MessageCount=0）— 刚创建的新会话 transcript.jsonl 为空，
    /// 不应被当作"最近历史会话"恢复（否则 -c 恢复空会话而非之前有内容的会话）。</summary>
    [Fact]
    public async Task Continue_SkipsEmptySession_MatchesNonEmptySession() {
        var currentProject = ProjectPath("cur", "repo");
        var service = CreateTranscriptService([
            ("empty-newest", currentProject, DateTime.UtcNow.AddMinutes(-1), 0),
            ("nonempty-older", currentProject, DateTime.UtcNow.AddHours(-2), 4),
        ]);

        var sessionId = await SessionResumeStep.FindMostRecentSessionIdAsync(service.Object, currentProject, CancellationToken.None);

        sessionId.Should().Be("nonempty-older", "空会话（MessageCount=0）应跳过，选有内容的较旧会话");
    }
}
