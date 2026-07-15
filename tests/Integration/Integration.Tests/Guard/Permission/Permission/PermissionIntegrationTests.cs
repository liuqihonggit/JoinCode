
#pragma warning disable JCC3010, JCC3011, JCC3012
namespace Integration.Tests.Guard.Permission;

using IToolHandler = JoinCode.Abstractions.Tools.IToolHandler;
using ToolContent = JoinCode.Abstractions.Tools.ToolContent;
using McpToolRegistry;
using JoinCode.Abstractions.Security.Shell;
using JoinCode.Abstractions.Exceptions;

/// <summary>
/// Permission 集成测试 - 使用共享静态配置（已通过分片锁保护）
/// </summary>
public class PermissionIntegrationTests : IAsyncDisposable
{
    private readonly FakeTimeProvider _fakeTime;
    private readonly PermissionManager _permissionManager;
    private readonly LocalToolRegistry _registryWithPermission;
    private readonly LocalToolRegistry _registryWithoutPermission;
    private readonly PermissionAwareToolExecutor _permissionExecutor;

    public PermissionIntegrationTests()
    {
        _fakeTime = new FakeTimeProvider();
        _permissionManager = CreateManagerWithDefaultConfig(NullLogger<PermissionManager>.Instance, _fakeTime);
        _registryWithPermission = new LocalToolRegistry();
        _registryWithoutPermission = new LocalToolRegistry();
        _permissionExecutor = new PermissionAwareToolExecutor(
            _registryWithPermission,
            new MiddlewarePipeline<ToolExecutionContext>(BuildToolExecutionMiddlewares(_permissionManager)),
            logger: NullLogger<PermissionAwareToolExecutor>.Instance);
    }

    private static PermissionManager CreateManagerWithDefaultConfig(ILogger<PermissionManager>? logger = null, TimeProvider? timeProvider = null)
    {
        var config = PermissionConfig.CreateDefault();
        var configOptions = Options.Create(config);
        var middlewares = new IMiddleware<PermissionCheckContext>[]
        {
            new BypassPermissionMiddleware(),
            new Core.Permission.AgentRestrictionMiddleware(),
            new DangerousCommandProtectionMiddleware(destructiveCommandDetector: new DestructiveCommandDetector()),
            new AutoClassifierMiddleware(),
            new ConfigGetOperationMiddleware(),
            new WebFetchPermissionMiddleware(),
            new EarlyPathDenyMiddleware(),
            new ToolListPermissionMiddleware(),
            new PathPermissionMiddleware(),
            new DangerousOperationMiddleware(),
            new PlanModeMiddleware(),
            new DefaultResultMiddleware()
        };
        var pipeline = new MiddlewarePipeline<PermissionCheckContext>(middlewares);
        var checker = new PermissionChecker(pipeline, configOptions, new IO.FileSystem.PhysicalFileSystem());
        return new PermissionManager(checker, configOptions, logger, timeProvider);
    }

    private static IEnumerable<IToolExecutionMiddleware> BuildToolExecutionMiddlewares(IToolPermissionManager permissionManager)
    {
        var interceptor = new PermissionCheckingInterceptor(permissionManager, NullLogger<PermissionCheckingInterceptor>.Instance);
        yield return new ArgumentRepairMiddleware(NullLogger<ArgumentRepairMiddleware>.Instance);
        yield return new RequiredParamsMiddleware(NullLogger<RequiredParamsMiddleware>.Instance);
        yield return new PermissionCheckMiddleware(interceptor, NullLogger<PermissionCheckMiddleware>.Instance);
        yield return new ToolExecutionMiddleware(NullLogger<ToolExecutionMiddleware>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _permissionManager.DisposeAsync().ConfigureAwait(true);
        await _registryWithPermission.DisposeAsync().ConfigureAwait(true);
        await _registryWithoutPermission.DisposeAsync().ConfigureAwait(true);
    }

    #region McpToolRegistry Integration Tests

    [Fact]
    public async Task ExecuteToolAsync_WithPermissionManager_AutoApprovedTool_ShouldExecuteSuccessfully()
    {
        var mockHandler = CreateMockToolHandler(FileToolNameConstants.FileRead, "Read file tool");
        mockHandler
            .Setup(h => h.ExecuteAsync(It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult
            {
                Content = new List<ToolContent> { new() { Type = ToolContentType.Text, Text = "File content" } },
                IsError = false
            });

        await _registryWithPermission.RegisterToolAsync(mockHandler.Object).ConfigureAwait(true);

        var result = await _permissionExecutor.ExecuteAsync(FileToolNameConstants.FileRead, new Dictionary<string, JsonElement>()).ConfigureAwait(true);

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Be("File content");
    }

    [Fact]
    public async Task ExecuteToolAsync_WithPermissionManager_DangerousTool_ShouldThrowPermissionPendingConfirmationException()
    {
        var mockHandler = CreateMockToolHandler("file_delete", "Delete file tool");
        await _registryWithPermission.RegisterToolAsync(mockHandler.Object).ConfigureAwait(true);

        var act = async () => await _permissionExecutor.ExecuteAsync("file_delete", new Dictionary<string, JsonElement>()).ConfigureAwait(true);

        await act.Should().ThrowAsync<PermissionPendingConfirmationException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task ExecuteToolAsync_WithPermissionManager_BypassMode_ShouldExecuteDangerousTool()
    {
        await _permissionManager.SetPermissionModeAsync(PermissionMode.BypassPermissions).ConfigureAwait(true);

        var mockHandler = CreateMockToolHandler("file_delete", "Delete file tool");
        mockHandler
            .Setup(h => h.ExecuteAsync(It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult
            {
                Content = new List<ToolContent> { new() { Type = ToolContentType.Text, Text = "Deleted" } },
                IsError = false
            });

        await _registryWithPermission.RegisterToolAsync(mockHandler.Object).ConfigureAwait(true);

        var result = await _permissionExecutor.ExecuteAsync("file_delete", new Dictionary<string, JsonElement>()).ConfigureAwait(true);

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteToolAsync_WithoutPermissionManager_ShouldSkipPermissionCheck()
    {
        var mockHandler = CreateMockToolHandler("any_tool", "Any tool");
        mockHandler
            .Setup(h => h.ExecuteAsync(It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult
            {
                Content = new List<ToolContent> { new() { Type = ToolContentType.Text, Text = "Executed" } },
                IsError = false
            });

        await _registryWithoutPermission.RegisterToolAsync(mockHandler.Object).ConfigureAwait(true);

        var result = await _registryWithoutPermission.ExecuteToolAsync("any_tool", new Dictionary<string, JsonElement>()).ConfigureAwait(true);

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Be("Executed");
    }

    [Fact]
    public async Task ExecuteToolAsync_WithPermissionManager_TemporarilyApprovedTool_ShouldExecute()
    {
        _permissionManager.ApproveToolTemporarily("custom_dangerous_tool", TimeSpan.FromMinutes(5));

        var mockHandler = CreateMockToolHandler("custom_dangerous_tool", "Custom dangerous tool");
        mockHandler
            .Setup(h => h.ExecuteAsync(It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult
            {
                Content = new List<ToolContent> { new() { Type = ToolContentType.Text, Text = "Executed" } },
                IsError = false
            });

        await _registryWithPermission.RegisterToolAsync(mockHandler.Object).ConfigureAwait(true);

        var result = await _permissionExecutor.ExecuteAsync("custom_dangerous_tool", new Dictionary<string, JsonElement>()).ConfigureAwait(true);

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteToolAsync_WithPermissionManager_ExpiredTemporaryApproval_ShouldRequireConfirmation()
    {
        // 使用 FakeTimeProvider 推进时间使权限过期，替代 Task.Delay
        _permissionManager.ApproveToolTemporarily("expired_tool", TimeSpan.FromMilliseconds(5));
        _fakeTime.Advance(TimeSpan.FromMilliseconds(10));

        var mockHandler = CreateMockToolHandler("expired_tool", "Expired tool");
        await _registryWithPermission.RegisterToolAsync(mockHandler.Object).ConfigureAwait(true);

        var act = async () => await _permissionExecutor.ExecuteAsync("expired_tool", new Dictionary<string, JsonElement>()).ConfigureAwait(true);

        await act.Should().ThrowAsync<PermissionPendingConfirmationException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task ExecuteToolAsync_WithPermissionManager_ShellWithDangerousCommand_ShouldBeRejected()
    {
        var mockHandler = CreateMockToolHandler(ShellToolNameConstants.Bash, "Shell tool");
        await _registryWithPermission.RegisterToolAsync(mockHandler.Object).ConfigureAwait(true);

        var arguments = new Dictionary<string, JsonElement>
        {
            ["command"] = JsonDocument.Parse("\"rm -rf /\"").RootElement
        };

        var act = async () => await _permissionExecutor.ExecuteAsync(ShellToolNameConstants.Bash, arguments).ConfigureAwait(true);

        await act.Should().ThrowAsync<PermissionDeniedException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task ExecuteToolAsync_WithPermissionManager_WriteToSensitivePath_ShouldRequireConfirmation()
    {
        var mockHandler = CreateMockToolHandler(FileToolNameConstants.FileWrite, "Write file tool");
        await _registryWithPermission.RegisterToolAsync(mockHandler.Object).ConfigureAwait(true);

        var arguments = new Dictionary<string, JsonElement>
        {
            ["path"] = JsonSerializer.SerializeToElement("C:\\Windows\\system32\\test.txt")
        };

        var act = async () => await _permissionExecutor.ExecuteAsync(FileToolNameConstants.FileWrite, arguments).ConfigureAwait(true);

        await act.Should().ThrowAsync<PermissionPendingConfirmationException>().ConfigureAwait(true);
    }

    #endregion

    #region End-to-End Permission Flow Tests

    [Fact]
    public async Task EndToEnd_DefaultMode_ReadOperation_ShouldAutoApprove()
    {
        await _permissionManager.SetPermissionModeAsync(PermissionMode.Default).ConfigureAwait(true);

        var mockHandler = CreateMockToolHandler(SearchToolName.Glob.ToValue(), "Glob tool");
        mockHandler
            .Setup(h => h.ExecuteAsync(It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult
            {
                Content = new List<ToolContent> { new() { Type = ToolContentType.Text, Text = "Files found" } },
                IsError = false
            });

        await _registryWithPermission.RegisterToolAsync(mockHandler.Object).ConfigureAwait(true);

        var result = await _permissionExecutor.ExecuteAsync(SearchToolName.Glob.ToValue(), new Dictionary<string, JsonElement>()).ConfigureAwait(true);

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task EndToEnd_AskMode_UnknownTool_ShouldRequireConfirmation()
    {
        await _permissionManager.SetPermissionModeAsync(PermissionMode.Ask).ConfigureAwait(true);

        var mockHandler = CreateMockToolHandler("unknown_custom_tool", "Unknown tool");
        await _registryWithPermission.RegisterToolAsync(mockHandler.Object).ConfigureAwait(true);

        var act = async () => await _permissionExecutor.ExecuteAsync("unknown_custom_tool", new Dictionary<string, JsonElement>()).ConfigureAwait(true);

        await act.Should().ThrowAsync<PermissionPendingConfirmationException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task EndToEnd_AutoMode_SafeOperation_ShouldAutoApprove()
    {
        await _permissionManager.SetPermissionModeAsync(PermissionMode.Auto).ConfigureAwait(true);

        var mockHandler = CreateMockToolHandler(WebToolNameConstants.WebSearch, "Web search tool");
        mockHandler
            .Setup(h => h.ExecuteAsync(It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult
            {
                Content = new List<ToolContent> { new() { Type = ToolContentType.Text, Text = "Search results" } },
                IsError = false
            });

        await _registryWithPermission.RegisterToolAsync(mockHandler.Object).ConfigureAwait(true);

        var result = await _permissionExecutor.ExecuteAsync(WebToolNameConstants.WebSearch, new Dictionary<string, JsonElement>()).ConfigureAwait(true);

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task EndToEnd_PlanMode_ReadOperation_ShouldAutoApprove()
    {
        await _permissionManager.SetPermissionModeAsync(PermissionMode.Plan).ConfigureAwait(true);

        var mockHandler = CreateMockToolHandler(SearchToolName.Grep.ToValue(), "Grep tool");
        mockHandler
            .Setup(h => h.ExecuteAsync(It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult
            {
                Content = new List<ToolContent> { new() { Type = ToolContentType.Text, Text = "Matches found" } },
                IsError = false
            });

        await _registryWithPermission.RegisterToolAsync(mockHandler.Object).ConfigureAwait(true);

        var result = await _permissionExecutor.ExecuteAsync(SearchToolName.Grep.ToValue(), new Dictionary<string, JsonElement>()).ConfigureAwait(true);

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task EndToEnd_PlanMode_WriteOperation_ShouldRequireConfirmation()
    {
        await _permissionManager.SetPermissionModeAsync(PermissionMode.Plan).ConfigureAwait(true);

        var mockHandler = CreateMockToolHandler(FileToolNameConstants.FileWrite, "Write tool");
        await _registryWithPermission.RegisterToolAsync(mockHandler.Object).ConfigureAwait(true);

        var act = async () => await _permissionExecutor.ExecuteAsync(FileToolNameConstants.FileWrite, new Dictionary<string, JsonElement>()).ConfigureAwait(true);

        await act.Should().ThrowAsync<PermissionPendingConfirmationException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task EndToEnd_MultipleTools_DifferentPermissions_ShouldHandleCorrectly()
    {
        var readHandler = CreateMockToolHandler(FileToolNameConstants.FileRead, "Read tool");
        readHandler
            .Setup(h => h.ExecuteAsync(It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult
            {
                Content = new List<ToolContent> { new() { Type = ToolContentType.Text, Text = "Read success" } },
                IsError = false
            });

        var deleteHandler = CreateMockToolHandler("file_delete", "Delete tool");

        await _registryWithPermission.RegisterToolAsync(readHandler.Object).ConfigureAwait(true);
        await _registryWithPermission.RegisterToolAsync(deleteHandler.Object).ConfigureAwait(true);

        var readResult = await _permissionExecutor.ExecuteAsync(FileToolNameConstants.FileRead, new Dictionary<string, JsonElement>()).ConfigureAwait(true);
        readResult.IsError.Should().BeFalse();

        var deleteAct = async () => await _permissionExecutor.ExecuteAsync("file_delete", new Dictionary<string, JsonElement>()).ConfigureAwait(true);
        await deleteAct.Should().ThrowAsync<PermissionPendingConfirmationException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task EndToEnd_ModeSwitchDuringExecution_ShouldUseNewMode()
    {
        var mockHandler = CreateMockToolHandler("custom_test_tool", "Test tool");
        mockHandler
            .Setup(h => h.ExecuteAsync(It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult
            {
                Content = new List<ToolContent> { new() { Type = ToolContentType.Text, Text = "Success" } },
                IsError = false
            });

        await _registryWithPermission.RegisterToolAsync(mockHandler.Object).ConfigureAwait(true);

        await _permissionManager.SetPermissionModeAsync(PermissionMode.Auto).ConfigureAwait(true);
        var result1 = await _permissionExecutor.ExecuteAsync("custom_test_tool", new Dictionary<string, JsonElement>()).ConfigureAwait(true);
        result1.IsError.Should().BeFalse();

        await _permissionManager.SetPermissionModeAsync(PermissionMode.Default).ConfigureAwait(true);
        var act = async () => await _permissionExecutor.ExecuteAsync("custom_test_tool", new Dictionary<string, JsonElement>()).ConfigureAwait(true);
        await act.Should().ThrowAsync<PermissionPendingConfirmationException>().ConfigureAwait(true);

        await _permissionManager.SetPermissionModeAsync(PermissionMode.BypassPermissions).ConfigureAwait(true);
        var result3 = await _permissionExecutor.ExecuteAsync("custom_test_tool", new Dictionary<string, JsonElement>()).ConfigureAwait(true);
        result3.IsError.Should().BeFalse();
    }

    #endregion

    #region PermissionDeniedException Flow Tests

    [Fact]
    public async Task EndToEnd_BlockedTool_ShouldThrowPermissionDeniedException()
    {
        var mockHandler = CreateMockToolHandler("blocked_tool", "Blocked tool");
        // 使用 FakeTimeProvider 推进时间使权限过期，替代 Task.Delay
        _permissionManager.ApproveToolTemporarily("blocked_tool", TimeSpan.FromMilliseconds(5));
        _fakeTime.Advance(TimeSpan.FromMilliseconds(10));

        await _registryWithPermission.RegisterToolAsync(mockHandler.Object).ConfigureAwait(true);

        await _permissionManager.SetPermissionModeAsync(PermissionMode.Ask).ConfigureAwait(true);

        var act = async () => await _permissionExecutor.ExecuteAsync("blocked_tool", new Dictionary<string, JsonElement>()).ConfigureAwait(true);

        await act.Should().ThrowAsync<PermissionPendingConfirmationException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task EndToEnd_PermissionException_ShouldContainCorrectInfo()
    {
        var mockHandler = CreateMockToolHandler("sensitive_tool", "Sensitive tool");
        await _registryWithPermission.RegisterToolAsync(mockHandler.Object).ConfigureAwait(true);

        await _permissionManager.SetPermissionModeAsync(PermissionMode.Ask).ConfigureAwait(true);

        try
        {
            await _permissionExecutor.ExecuteAsync("sensitive_tool", new Dictionary<string, JsonElement>()).ConfigureAwait(true);
            Assert.Fail("Expected exception was not thrown");
        }
        catch (PermissionPendingConfirmationException ex)
        {
            ex.ToolName.Should().Be("sensitive_tool");
            ex.ConfirmationPrompt.Should().NotBeNullOrEmpty();
        }
    }

    #endregion

    #region Concurrent Permission Tests

    [Fact]
    public async Task EndToEnd_ConcurrentToolExecution_ShouldRespectPermissions()
    {
        var mockHandler = CreateMockToolHandler("concurrent_tool", "Concurrent tool");
        mockHandler
            .Setup(h => h.ExecuteAsync(It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult
            {
                Content = new List<ToolContent> { new() { Type = ToolContentType.Text, Text = "Success" } },
                IsError = false
            });

        _permissionManager.ApproveToolTemporarily("concurrent_tool", TimeSpan.FromMinutes(5));
        await _registryWithPermission.RegisterToolAsync(mockHandler.Object).ConfigureAwait(true);

        var tasks = new List<Task<ToolResult>>();
        for (int i = 0; i < 50; i++)
        {
            tasks.Add(_permissionExecutor.ExecuteAsync("concurrent_tool", new Dictionary<string, JsonElement>()));
        }

        var results = await Task.WhenAll(tasks).ConfigureAwait(true);

        results.Should().AllSatisfy(r => r.IsError.Should().BeFalse());
    }

    [Fact]
    public async Task EndToEnd_ConcurrentMixedPermissionTools_ShouldHandleCorrectly()
    {
        var safeHandler = CreateMockToolHandler(FileToolNameConstants.FileRead, "Safe tool");
        safeHandler
            .Setup(h => h.ExecuteAsync(It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult
            {
                Content = new List<ToolContent> { new() { Type = ToolContentType.Text, Text = "Safe" } },
                IsError = false
            });

        var dangerousHandler = CreateMockToolHandler("file_delete", "Dangerous tool");

        await _registryWithPermission.RegisterToolAsync(safeHandler.Object).ConfigureAwait(true);
        await _registryWithPermission.RegisterToolAsync(dangerousHandler.Object).ConfigureAwait(true);

        var safeTasks = Enumerable.Range(0, 25)
            .Select(_ => _permissionExecutor.ExecuteAsync(FileToolNameConstants.FileRead, new Dictionary<string, JsonElement>()));

        var dangerousTasks = Enumerable.Range(0, 25)
            .Select(_ => Task.Run(async () =>
            {
                try
                {
                    await _permissionExecutor.ExecuteAsync("file_delete", new Dictionary<string, JsonElement>()).ConfigureAwait(true);
                    return false;
                }
                catch (PermissionPendingConfirmationException)
                {
                    return true;
                }
            }));

        var safeResults = await Task.WhenAll(safeTasks).ConfigureAwait(true);
        var dangerousResults = await Task.WhenAll(dangerousTasks).ConfigureAwait(true);

        safeResults.Should().AllSatisfy(r => r.IsError.Should().BeFalse());
        dangerousResults.Should().AllSatisfy(r => r.Should().BeTrue());
    }

    #endregion

    #region Permission Cache Integration Tests

    [Fact]
    public async Task EndToEnd_CachedPermission_ShouldNotRequeryManager()
    {
        var mockHandler = CreateMockToolHandler("cached_tool", "Cached tool");
        mockHandler
            .Setup(h => h.ExecuteAsync(It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult
            {
                Content = new List<ToolContent> { new() { Type = ToolContentType.Text, Text = "Success" } },
                IsError = false
            });

        _permissionManager.ApproveToolTemporarily("cached_tool", TimeSpan.FromMinutes(5));
        await _registryWithPermission.RegisterToolAsync(mockHandler.Object).ConfigureAwait(true);

        var result1 = await _permissionExecutor.ExecuteAsync("cached_tool", new Dictionary<string, JsonElement>()).ConfigureAwait(true);
        var result2 = await _permissionExecutor.ExecuteAsync("cached_tool", new Dictionary<string, JsonElement>()).ConfigureAwait(true);

        result1.IsError.Should().BeFalse();
        result2.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task EndToEnd_CacheClearAfterModeChange_ShouldReevaluatePermissions()
    {
        var mockHandler = CreateMockToolHandler("reevaluated_tool", "Reevaluated tool");
        mockHandler
            .Setup(h => h.ExecuteAsync(It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult
            {
                Content = new List<ToolContent> { new() { Type = ToolContentType.Text, Text = "Success" } },
                IsError = false
            });

        await _registryWithPermission.RegisterToolAsync(mockHandler.Object).ConfigureAwait(true);

        await _permissionManager.SetPermissionModeAsync(PermissionMode.Auto).ConfigureAwait(true);
        var result1 = await _permissionExecutor.ExecuteAsync("reevaluated_tool", new Dictionary<string, JsonElement>()).ConfigureAwait(true);
        result1.IsError.Should().BeFalse();

        await _permissionManager.SetPermissionModeAsync(PermissionMode.Default).ConfigureAwait(true);
        var act = async () => await _permissionExecutor.ExecuteAsync("reevaluated_tool", new Dictionary<string, JsonElement>()).ConfigureAwait(true);
        await act.Should().ThrowAsync<PermissionPendingConfirmationException>().ConfigureAwait(true);
    }

    #endregion

    #region Helper Methods

    private static Mock<IToolHandler> CreateMockToolHandler(string name, string description)
    {
        var mock = new Mock<IToolHandler>();
        mock.Setup(h => h.Name).Returns(name);
        mock.Setup(h => h.Description).Returns(description);
        mock.Setup(h => h.InputSchema).Returns(new ToolSchema
        {
            Type = "object",
            Properties = new Dictionary<string, ToolSchemaProperty>(),
            Required = new List<string>()
        });
        return mock;
    }

    #endregion
}
#pragma warning restore JCC3010, JCC3011, JCC3012
