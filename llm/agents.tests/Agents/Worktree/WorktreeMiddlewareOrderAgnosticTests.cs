namespace JoinCode.Agents.Tests.Worktree;

/// <summary>
/// Worktree 中间件顺序无关性测试 — 验证综合防御策略:
/// <para>第一层防线:Order 属性 + 管道构建处 OrderBy 保证业务期望执行顺序</para>
/// <para>第二层防线:WorktreeContextEnricher 让每个中间件自给自足,即使顺序被打乱也能正确填充依赖字段</para>
/// <para>根因:源码生成器按类名字母排序注册 DI,导致 ConfigMiddleware(字母 C)在 CreateMiddleware 之前执行,</para>
/// <para>此时 context.GitRoot 和 context.WorktreePath 均为空,settings.local.json 复制因 sourcePath==destPath 跳过</para>
/// </summary>
public class WorktreeMiddlewareOrderAgnosticTests {
    // === 第一层防线:Order 属性值验证 ===

    [Fact]
    public void Order_WorktreeValidationMiddleware_Is100() {
        var middleware = CreateValidationMiddleware();
        middleware.Order.Should().Be(100);
    }

    [Fact]
    public void Order_WorktreeGitRootMiddleware_Is200() {
        var middleware = CreateGitRootMiddleware();
        middleware.Order.Should().Be(200);
    }

    [Fact]
    public void Order_WorktreeRecoveryMiddleware_Is300() {
        var middleware = CreateRecoveryMiddleware();
        middleware.Order.Should().Be(300);
    }

    [Fact]
    public void Order_WorktreeGitInfoMiddleware_Is400() {
        var middleware = CreateGitInfoMiddleware();
        middleware.Order.Should().Be(400);
    }

    [Fact]
    public void Order_WorktreeCreateMiddleware_Is500() {
        var (middleware, _, _) = CreateCreateMiddleware();
        middleware.Order.Should().Be(500);
    }

    [Fact]
    public void Order_WorktreeConfigMiddleware_Is600() {
        var middleware = CreateConfigMiddleware();
        middleware.Order.Should().Be(600);
    }

    [Fact]
    public void Order_WorktreeSessionSaveMiddleware_Is700() {
        var middleware = CreateSessionSaveMiddleware();
        middleware.Order.Should().Be(700);
    }

    [Fact]
    public void Order_AllMiddlewares_HaveDistinctAscendingOrder() {
        var middlewares = new IWorktreeCreateMiddleware[]
        {
            CreateValidationMiddleware(),
            CreateGitRootMiddleware(),
            CreateRecoveryMiddleware(),
            CreateGitInfoMiddleware(),
            CreateCreateMiddleware().middleware,
            CreateConfigMiddleware(),
            CreateSessionSaveMiddleware(),
        };

        var orders = middlewares.Select(m => m.Order).ToList();
        orders.Should().BeInAscendingOrder();
        orders.Should().HaveCount(7);
        orders.Distinct().Should().HaveCount(7);
    }

    // === 第二层防线:顺序无关的自给自足测试 ===

    /// <summary>
    /// 核心回归测试:ConfigMiddleware 在 GitRoot+WorktreePath 为空时(模拟排在第一位执行),
    /// 通过 WorktreeContextEnricher 自给自足填充,sourcePath != destPath,不会错误跳过复制
    /// </summary>
    [Fact]
    public async Task ConfigMiddleware_EmptyGitRootAndWorktreePath_AutoEnrichesAndDoesNotSkipCopy() {
        var fs = new InMemoryFileOperationService();
        var gitRoot = "/repo";
        fs.CreateDirectory(gitRoot);
        var settingsDir = fs.CombinePath(gitRoot, ".jcc");
        fs.CreateDirectory(settingsDir);
        var settingsFile = fs.CombinePath(settingsDir, "settings.local.json");
        await fs.WriteFileAsync(settingsFile, "{}");

        var opsMock = new Mock<IWorktreePipelineOperations>();
        var middleware = new WorktreeConfigMiddleware(
            fs,
            new Lazy<IWorktreePipelineOperations>(() => opsMock.Object));

        var context = new WorktreeCreateContext {
            AgentId = "test-agent",
            GitRoot = "",
            WorktreePath = "",
            OriginalCwd = gitRoot,
            Options = new WorktreeOptions(),
        };

        await middleware.InvokeAsync(context, NextNoOp, default);

        context.GitRoot.Should().NotBeEmpty("ConfigMiddleware 应通过 Enricher 自给自足填充 GitRoot");
        context.WorktreePath.Should().NotBeEmpty("ConfigMiddleware 应通过 Enricher 自给自足填充 WorktreePath");
        context.GitRoot.Should().Be(gitRoot);
        context.WorktreePath.Should().Be(AgentWorktreeSession.GenerateWorktreePath(gitRoot, "test-agent"));

        var destSettingsFile = fs.CombinePath(context.WorktreePath, ".jcc", "settings.local.json");
        fs.FileExists(destSettingsFile).Should().BeTrue("配置文件应已复制到 worktree 中");
    }

    /// <summary>
    /// RecoveryMiddleware 在 GitRoot 为空时(模拟排在 GitRoot 之前执行),
    /// 通过 Enricher 自给自足填充 GitRoot,不会用空 GitRoot 生成无效路径
    /// </summary>
    [Fact]
    public async Task RecoveryMiddleware_EmptyGitRoot_AutoEnrichesBeforeGeneratingPath() {
        var fs = new InMemoryFileOperationService();
        var gitRoot = "/repo";
        fs.CreateDirectory(gitRoot);

        var opsMock = new Mock<IWorktreePipelineOperations>();
        var clockMock = new Mock<IClockService>();
        clockMock.Setup(x => x.GetUtcNow()).Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var middleware = new WorktreeRecoveryMiddleware(
            fs,
            new Lazy<IWorktreePipelineOperations>(() => opsMock.Object),
            clockMock.Object);

        var context = new WorktreeCreateContext {
            AgentId = "test-agent",
            GitRoot = "",
            OriginalCwd = gitRoot,
            Options = new WorktreeOptions(),
        };

        await middleware.InvokeAsync(context, NextNoOp, default);

        context.GitRoot.Should().NotBeEmpty("RecoveryMiddleware 应通过 Enricher 自给自足填充 GitRoot");
        context.GitRoot.Should().Be(gitRoot);
    }

    /// <summary>
    /// GitInfoMiddleware 在 GitRoot 为空时(模拟排在 GitRoot 之前执行),
    /// 通过 Enricher 自给自足填充 GitRoot,不会用空 GitRoot 执行 git 命令
    /// </summary>
    [Fact]
    public async Task GitInfoMiddleware_EmptyGitRoot_AutoEnrichesBeforeGitCommand() {
        var fs = new InMemoryFileOperationService();
        var gitRoot = "/repo";
        fs.CreateDirectory(gitRoot);

        var opsMock = new Mock<IWorktreePipelineOperations>();
        opsMock.Setup(x => x.GetCurrentBranchAsync(It.IsAny<string>())).ReturnsAsync("main");
        opsMock.Setup(x => x.GetHeadCommitShaAsync(It.IsAny<string>())).ReturnsAsync("abc123");
        opsMock.Setup(x => x.GetDefaultBranchAsync(It.IsAny<string>())).ReturnsAsync("main");
        opsMock.Setup(x => x.ResolveRefAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((string?)null);
        opsMock.Setup(x => x.ExecuteGitCommandAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitCommandResult { Success = true });

        var middleware = new WorktreeGitInfoMiddleware(
            new Lazy<IWorktreePipelineOperations>(() => opsMock.Object),
            fs);

        var context = new WorktreeCreateContext {
            AgentId = "test-agent",
            GitRoot = "",
            OriginalCwd = gitRoot,
            Options = new WorktreeOptions(),
        };

        await middleware.InvokeAsync(context, NextNoOp, default);

        context.GitRoot.Should().NotBeEmpty("GitInfoMiddleware 应通过 Enricher 自给自足填充 GitRoot");
        context.GitRoot.Should().Be(gitRoot);
        context.OriginalBranch.Should().Be("main");
    }

    /// <summary>
    /// SessionSaveMiddleware 在 WorktreePath 为空时(模拟排在 Create 之前执行),
    /// 通过 Enricher 自给自足填充 WorktreePath,不会保存空路径的会话
    /// </summary>
    [Fact]
    public async Task SessionSaveMiddleware_EmptyWorktreePath_AutoEnrichesBeforeSaving() {
        var fs = new InMemoryFileOperationService();
        var gitRoot = "/repo";
        fs.CreateDirectory(gitRoot);

        var opsMock = new Mock<IWorktreePipelineOperations>();
        var clockMock = new Mock<IClockService>();
        clockMock.Setup(x => x.GetUtcNow()).Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var middleware = new WorktreeSessionSaveMiddleware(
            new Lazy<IWorktreePipelineOperations>(() => opsMock.Object),
            fs,
            clockMock.Object);

        var context = new WorktreeCreateContext {
            AgentId = "test-agent",
            GitRoot = gitRoot,
            WorktreePath = "",
            BranchName = "",
            OriginalCwd = gitRoot,
            Options = new WorktreeOptions(),
        };

        await middleware.InvokeAsync(context, NextNoOp, default);

        context.WorktreePath.Should().NotBeEmpty("SessionSaveMiddleware 应通过 Enricher 自给自足填充 WorktreePath");
        context.BranchName.Should().NotBeEmpty("SessionSaveMiddleware 应通过 Enricher 自给自足填充 BranchName");
        context.Session.Should().NotBeNull();
        context.Session!.WorktreePath.Should().NotBeEmpty();
    }

    // === WorktreeContextEnricher 单元测试 ===

    [Fact]
    public void Enricher_EnsureGitRoot_EmptyFillsFromOriginalCwd() {
        var fs = new InMemoryFileOperationService();
        var context = new WorktreeCreateContext {
            AgentId = "test",
            GitRoot = "",
            OriginalCwd = "/custom/cwd",
        };

        WorktreeContextEnricher.EnsureGitRoot(context, fs);

        context.GitRoot.Should().Be("/custom/cwd");
        context.OriginalCwd.Should().Be("/custom/cwd");
    }

    [Fact]
    public void Enricher_EnsureGitRoot_EmptyOriginalCwdFillsFromCurrentDirectory() {
        var fs = new InMemoryFileOperationService();
        var context = new WorktreeCreateContext {
            AgentId = "test",
            GitRoot = "",
            OriginalCwd = "",
        };

        WorktreeContextEnricher.EnsureGitRoot(context, fs);

        context.GitRoot.Should().NotBeEmpty();
        context.OriginalCwd.Should().NotBeEmpty();
    }

    [Fact]
    public void Enricher_EnsureGitRoot_NonEmptyDoesNotOverwrite() {
        var fs = new InMemoryFileOperationService();
        var context = new WorktreeCreateContext {
            AgentId = "test",
            GitRoot = "/existing/root",
            OriginalCwd = "/custom/cwd",
        };

        WorktreeContextEnricher.EnsureGitRoot(context, fs);

        context.GitRoot.Should().Be("/existing/root");
    }

    [Fact]
    public void Enricher_EnsureWorktreePath_EmptyGeneratesFromGitRoot() {
        var context = new WorktreeCreateContext {
            AgentId = "test-agent",
            GitRoot = "/repo",
            WorktreePath = "",
        };

        WorktreeContextEnricher.EnsureWorktreePath(context);

        var expected = AgentWorktreeSession.GenerateWorktreePath("/repo", "test-agent");
        context.WorktreePath.Should().Be(expected);
    }

    [Fact]
    public void Enricher_EnsureWorktreePath_EmptyGitRootDoesNothing() {
        var context = new WorktreeCreateContext {
            AgentId = "test-agent",
            GitRoot = "",
            WorktreePath = "",
        };

        WorktreeContextEnricher.EnsureWorktreePath(context);

        context.WorktreePath.Should().BeEmpty("GitRoot 为空时不应生成无效路径");
    }

    [Fact]
    public void Enricher_EnsureWorktreePath_NonEmptyDoesNotOverwrite() {
        var context = new WorktreeCreateContext {
            AgentId = "test-agent",
            GitRoot = "/repo",
            WorktreePath = "/custom/path",
        };

        WorktreeContextEnricher.EnsureWorktreePath(context);

        context.WorktreePath.Should().Be("/custom/path");
    }

    [Fact]
    public void Enricher_EnsureBranchName_EmptyGeneratesFromAgentId() {
        var context = new WorktreeCreateContext {
            AgentId = "test-agent",
            BranchName = "",
        };

        WorktreeContextEnricher.EnsureBranchName(context);

        context.BranchName.Should().Be("worktree-test-agent");
    }

    [Fact]
    public void Enricher_EnsureAllPaths_FillsAllThree() {
        var fs = new InMemoryFileOperationService();
        var context = new WorktreeCreateContext {
            AgentId = "test-agent",
            GitRoot = "",
            WorktreePath = "",
            BranchName = "",
            OriginalCwd = "/repo",
        };

        WorktreeContextEnricher.EnsureAllPaths(context, fs);

        context.GitRoot.Should().Be("/repo");
        context.WorktreePath.Should().Be(AgentWorktreeSession.GenerateWorktreePath("/repo", "test-agent"));
        context.BranchName.Should().Be("worktree-test-agent");
    }

    // === 辅助方法 ===

    private static Task NextNoOp(WorktreeCreateContext ctx, CancellationToken ct) => Task.CompletedTask;

    private static WorktreeValidationMiddleware CreateValidationMiddleware() => new();

    private static WorktreeGitRootMiddleware CreateGitRootMiddleware() {
        var fs = new InMemoryFileOperationService();
        return new WorktreeGitRootMiddleware(fs, fs.FileSystem);
    }

    private static WorktreeRecoveryMiddleware CreateRecoveryMiddleware() {
        var fs = new InMemoryFileOperationService();
        var opsMock = new Mock<IWorktreePipelineOperations>();
        var clockMock = new Mock<IClockService>();
        return new WorktreeRecoveryMiddleware(
            fs,
            new Lazy<IWorktreePipelineOperations>(() => opsMock.Object),
            clockMock.Object);
    }

    private static WorktreeGitInfoMiddleware CreateGitInfoMiddleware() {
        var fs = new InMemoryFileOperationService();
        var opsMock = new Mock<IWorktreePipelineOperations>();
        return new WorktreeGitInfoMiddleware(
            new Lazy<IWorktreePipelineOperations>(() => opsMock.Object),
            fs);
    }

    private static (WorktreeCreateMiddleware middleware, Mock<IWorktreePipelineOperations> opsMock, List<string> capturedArgs) CreateCreateMiddleware() {
        var capturedArgs = new List<string>();
        var opsMock = new Mock<IWorktreePipelineOperations>();
        opsMock.Setup(x => x.HasLocalBranchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        opsMock.Setup(x => x.ExecuteGitCommandAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, args, _) => capturedArgs.Add(args))
            .ReturnsAsync(new GitCommandResult { Success = true });

        var fs = new InMemoryFileOperationService();
        var clockMock = new Mock<IClockService>();
        clockMock.Setup(x => x.GetUtcNow()).Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var middleware = new WorktreeCreateMiddleware(
            new Lazy<IWorktreePipelineOperations>(() => opsMock.Object),
            fs,
            clockMock.Object);
        return (middleware, opsMock, capturedArgs);
    }

    private static WorktreeConfigMiddleware CreateConfigMiddleware() {
        var fs = new InMemoryFileOperationService();
        var opsMock = new Mock<IWorktreePipelineOperations>();
        return new WorktreeConfigMiddleware(
            fs,
            new Lazy<IWorktreePipelineOperations>(() => opsMock.Object));
    }

    private static WorktreeSessionSaveMiddleware CreateSessionSaveMiddleware() {
        var fs = new InMemoryFileOperationService();
        var opsMock = new Mock<IWorktreePipelineOperations>();
        var clockMock = new Mock<IClockService>();
        return new WorktreeSessionSaveMiddleware(
            new Lazy<IWorktreePipelineOperations>(() => opsMock.Object),
            fs,
            clockMock.Object);
    }
}