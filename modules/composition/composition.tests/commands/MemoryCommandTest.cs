namespace Composition.Tests.Commands;

/// <summary>
/// /memory 命令单元测试 — 覆盖无参非交互文件列表/未知操作错误引导/add 无参与服务缺失降级/
/// add 主路径(查重+落库)/相似记忆拦截/类型与标签选项解析/search 无参用法与结果渲染/
/// db 列表/stats 统计/health 健康报告与建议/cleanup 天数选项解析/open 非交互目录提示/
/// edit 索引与越界回退
/// <para>MemoryManagementService 用 Moq 桩经 CommandServices.MemoryManagementService 注入,FileSystem 用内存实现</para>
/// </summary>
[Collection("command-output")]
public sealed class MemoryCommandTest {
    private static Mock<IMemoryManagementService> CreateMemoryService() {
        var service = new Mock<IMemoryManagementService>();
        service.Setup(s => s.ScanMemoriesAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryScanResult { TotalMemories = 0 });
        service.Setup(s => s.GetHealthReportAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryHealthReport { TotalMemories = 10, HealthyMemories = 8, NeedsAttention = 2, ShouldArchive = 2, ShouldDelete = 1, AverageHealthScore = 85.0 });
        service.Setup(s => s.GetMemoryAgeInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        service.Setup(s => s.CleanupOldMemoriesAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryCleanupResult { CheckedCount = 10, ArchivedCount = 2, DeletedCount = 1, RetainedCount = 7 });
        service.Setup(s => s.AddMemoryAsync(It.IsAny<string>(), It.IsAny<MemoryType>(), It.IsAny<string?>(), It.IsAny<List<string>?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("mem-001");
        return service;
    }

    private static (MemoryCommand Command, ChatCommandContext Context) Arrange(
        string arguments,
        Mock<IMemoryManagementService>? memoryService = null,
        IFileSystem? fileSystem = null,
        Mock<IProcessService>? processService = null) {
        var services = new ServiceCollection();
        if (processService is not null) services.AddSingleton(processService.Object);
        var commandServices = new CommandServices {
            ChatService = Mock.Of<IChatService>(),
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = fileSystem ?? TestFileSystem.Current,
            ServiceProvider = services.BuildServiceProvider(),
            MemoryManagementService = memoryService?.Object,
        };
        var context = new ChatCommandContext {
            Arguments = arguments,
            SessionId = "test-session",
            CancellationToken = CancellationToken.None,
            Services = new CommandServiceProvider(commandServices),
        };
        return (new MemoryCommand(), context);
    }

    private static DetailedScoredMemory Scored(string id, string content, double score, MemoryType type = MemoryType.User) => new() {
        Memory = new MemoryEntry { Id = id, Type = type, Content = content },
        RelevanceScore = score,
        MatchReason = "关键词命中",
    };

    [Fact]
    public async Task Execute_NoArgs_NonInteractive_ShowsFileList() {
        var fs = TestFileSystem.Current;
        var (cmd, context) = Arrange("", fileSystem: fs);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("── 记忆文件 ──", "非交互(测试环境)回退纯文本列表");
        output.Should().Contain("User memory");
        output.Should().Contain("Project memory");
        output.Should().Contain("Project AGENTS");
        output.Should().Contain("Project CLAUDE");
        output.Should().Contain("Auto-memory folder");
        output.Should().Contain("使用 /memory edit <编号> 打开编辑");
    }

    [Fact]
    public async Task Execute_UnknownAction_ShowsErrorAndAvailableActions() {
        var (cmd, context) = Arrange("bogus");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("未知操作：bogus");
        output.Should().Contain("可用操作：edit, open, add, search, db, stats, health, cleanup",
            "错误提示应列出全部有效子命令引导用户");
    }

    [Fact]
    public async Task Execute_Add_WithoutArguments_ShowsUsage() {
        var (cmd, context) = Arrange("add", memoryService: CreateMemoryService());

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("用法：/memory add <内容>");
        output.Should().NotContain("记忆已添加");
    }

    [Fact]
    public async Task Execute_Add_WithoutService_ShowsServiceUnavailable() {
        var (cmd, context) = Arrange("add 记住这个", memoryService: null);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("记忆服务缺失应降级提示而非崩溃");
        output.Should().Contain("记忆服务不可用");
    }

    [Fact]
    public async Task Execute_Add_WithService_PersistsMemory() {
        var memoryService = CreateMemoryService();
        var (cmd, context) = Arrange("add 记住这个要点", memoryService: memoryService);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        memoryService.Verify(s => s.AddMemoryAsync("记住这个要点", MemoryType.User, null, null, null, It.IsAny<CancellationToken>()), Times.Once,
            "add 应把剩余参数作为内容落库(默认 User 类型)");
        output.Should().Contain("记忆已添加 [mem-001]");
        output.Should().Contain("内容：记住这个要点");
    }

    [Fact]
    public async Task Execute_Add_SimilarMemoryAboveThreshold_ShowsWarningWithoutAdding() {
        var memoryService = CreateMemoryService();
        memoryService.Setup(s => s.ScanMemoriesAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryScanResult { TotalMemories = 1, RelevantMemories = [Scored("mem-001", "记住这个", 6.0)] });
        var (cmd, context) = Arrange("add 记住这个", memoryService: memoryService);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("发现相似记忆 [mem-001]：");
        output.Should().Contain("  相关度：6.0");
        memoryService.Verify(s => s.AddMemoryAsync(It.IsAny<string>(), It.IsAny<MemoryType>(), It.IsAny<string?>(), It.IsAny<List<string>?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never,
            "相似度 > 5.0 应拦截重复添加");
    }

    /// <summary>生产 bug 登记(TASK065 ai 域): --type 选项小写输入(如 feedback)时
    /// AddMemoryAsync 的 content.Replace($"--type {type}", "") 用解析后的枚举名(Feedback)
    /// 做大小写敏感匹配,无法剥离小写原文,选项文本残留进记忆内容与查重 query。
    /// 本测试按 usage 提示的规范大写形态(Feedback)验证剥离正路径;修复 Replace 为
    /// 忽略大小写后应补充小写形态用例。</summary>
    [Fact]
    public async Task Execute_Add_WithTagsAndType_ParsesOptions() {
        var memoryService = CreateMemoryService();
        var (cmd, context) = Arrange("add 关键结论 --type Feedback --tags rust,perf", memoryService: memoryService);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        memoryService.Verify(s => s.AddMemoryAsync("关键结论", MemoryType.Feedback, null, new List<string> { "rust", "perf" }, null, It.IsAny<CancellationToken>()), Times.Once,
            "--type/--tags 选项应从内容中剥离并解析(类型值大小写不敏感)");
        output.Should().Contain("类型：Feedback");
        output.Should().Contain("标签：rust, perf");
    }

    [Theory]
    [InlineData("search keyword")]
    [InlineData("db")]
    [InlineData("stats")]
    [InlineData("health")]
    [InlineData("cleanup")]
    public async Task Execute_ServiceDependentActions_WithoutService_ShowsServiceUnavailable(string action) {
        var (cmd, context) = Arrange(action, memoryService: null);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("记忆服务不可用", $"{action} 子命令在服务缺失时应统一降级提示");
    }

    [Fact]
    public async Task Execute_Search_WithoutArguments_ShowsUsage() {
        var (cmd, context) = Arrange("search", memoryService: CreateMemoryService());

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("用法：/memory search <关键词>");
    }

    [Fact]
    public async Task Execute_Search_NoResults_ShowsNoResults() {
        var (cmd, context) = Arrange("search query", memoryService: CreateMemoryService());

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("未找到相关记忆");
    }

    [Fact]
    public async Task Execute_Search_WithResults_ShowsScoreAndContent() {
        var memoryService = CreateMemoryService();
        memoryService.Setup(s => s.ScanMemoriesAsync("query", null, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryScanResult { TotalMemories = 1, RelevantMemories = [Scored("mem-001", "相关记忆内容", 7.5)] });
        var (cmd, context) = Arrange("search query", memoryService: memoryService);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("=== 搜索记忆：'query' ===");
        output.Should().Contain("[mem-001] [User]   相关度：7.5");
        output.Should().Contain("  相关记忆内容");
        output.Should().Contain("关键词命中");
        output.Should().Contain("共找到 1 条相关记忆");
    }

    [Fact]
    public async Task Execute_Db_ListsMemories() {
        var memoryService = CreateMemoryService();
        memoryService.Setup(s => s.ScanMemoriesAsync("*", null, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryScanResult { TotalMemories = 2, RelevantMemories = [Scored("mem-001", "第一条", 8.0), Scored("mem-002", "第二条", 7.0, MemoryType.Feedback)] });
        var (cmd, context) = Arrange("db", memoryService: memoryService);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("=== 数据库记忆 ===");
        output.Should().Contain("[mem-001] [User]");
        output.Should().Contain("[mem-002] [Feedback]");
        output.Should().Contain("共 2 条记忆");
    }

    [Fact]
    public async Task Execute_Stats_ShowsHealthReport() {
        var (cmd, context) = Arrange("stats", memoryService: CreateMemoryService());

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("=== 记忆统计 ===");
        output.Should().Contain("总记忆数：10");
        output.Should().Contain("健康记忆：8");
        output.Should().Contain("需要关注：2");
        output.Should().Contain("建议归档：2");
        output.Should().Contain("建议删除：1");
        output.Should().Contain("平均健康分：85.0");
    }

    [Fact]
    public async Task Execute_Health_ShowsHealthBarAndSuggestions() {
        var (cmd, context) = Arrange("health", memoryService: CreateMemoryService());

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("=== 记忆健康报告 ===");
        output.Should().Contain("健康度：85.0/100");
        output.Should().Contain("建议操作：");
        output.Should().Contain("归档：2 条记忆");
        output.Should().Contain("删除：1 条记忆");
        output.Should().Contain("使用 /memory cleanup 执行清理");
    }

    [Fact]
    public async Task Execute_Cleanup_ParsesDayOptionsAndCallsService() {
        var memoryService = CreateMemoryService();
        var (cmd, context) = Arrange("cleanup --archive-days 30 --delete-days 90", memoryService: memoryService);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        memoryService.Verify(s => s.CleanupOldMemoriesAsync(30, 90, It.IsAny<CancellationToken>()), Times.Once,
            "--archive-days/--delete-days 选项应解析传给清理服务");
        output.Should().Contain("清理完成");
        output.Should().Contain("  检查：10");
        output.Should().Contain("  归档：2");
        output.Should().Contain("  删除：1");
        output.Should().Contain("  保留：7");
    }

    [Fact]
    public async Task Execute_Open_NonInteractive_ShowsDirectoryPath() {
        var (cmd, context) = Arrange("open");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("记忆目录：", "非交互(测试环境)回退打印目录路径而非真实打开");
        output.Should().Contain("memories");
    }

    [Fact]
    public async Task Execute_Edit_WithValidIndex_CreatesAndOpensFirstFile() {
        var fs = TestFileSystem.Current;
        var userRulesPath = Path.Combine(AppDataConstants.Paths.JccDirectory, AppDataConstants.RulesFolderName, AppDataConstants.ProjectRulesFileName);
        var (cmd, context) = Arrange("edit 1", fileSystem: fs);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("打开：", "非交互(测试环境)回退打印编辑器提示而非真实启动编辑器");
        fs.FileExists(userRulesPath).Should().BeTrue("edit 应确保目标记忆文件存在(不存在则创建)");
    }

    [Fact]
    public async Task Execute_Edit_WithOutOfRangeIndex_FallsBackToFirstFile() {
        var fs = TestFileSystem.Current;
        var userRulesPath = Path.Combine(AppDataConstants.Paths.JccDirectory, AppDataConstants.RulesFolderName, AppDataConstants.ProjectRulesFileName);
        var (cmd, context) = Arrange("edit 99", fileSystem: fs);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("打开：");
        fs.FileExists(userRulesPath).Should().BeTrue("越界索引应回退打开第一个记忆文件");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new MemoryCommand();
        cmd.Name.Should().Be("memory");
        cmd.Usage.Should().Be("/memory [edit|open|add|search|db|stats|health|cleanup]");
        cmd.Aliases.Should().Contain("mem");
        cmd.Description.Should().Contain("记忆");
    }
}
