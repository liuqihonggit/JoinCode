
namespace Guard.Tests.Permission.Permission;

/// <summary>
/// DeleteProtectionMiddleware 单元测试 — 验证文件删除保护机制
/// Auto/Default 模式: 拒绝删除 + 引导使用 move 命令
/// Ask 模式: 提示用户确认 + 建议移动到 .xxx/
/// Plan 模式: 拒绝删除
/// 非 FileDelete 工具: 放行
/// </summary>
public sealed class DeleteProtectionMiddlewareTests
{
    private static readonly IDeleteOperationDetector[] FileDeleteOnlyDetectors = [new FileDeleteDetector()];

    private static MiddlewareDelegate<PermissionCheckContext> CreateNext()
    {
        return new MiddlewareDelegate<PermissionCheckContext>((ctx, ct) => Task.CompletedTask);
    }

    private static PermissionCheckContext CreateContext(
        string toolName,
        PermissionMode mode,
        Dictionary<string, JsonElement>? arguments = null)
    {
        return new PermissionCheckContext
        {
            ToolName = toolName,
            Arguments = arguments,
            CurrentMode = mode,
            Config = PermissionConfig.CreateDefault(),
            AutoApprovedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            AutoRejectedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        };
    }

    private static Dictionary<string, JsonElement> CreateFilePathArgument(string path)
    {
        var element = JsonSerializer.Deserialize<JsonElement>($"\"{path}\"");
        return new Dictionary<string, JsonElement> { ["file_path"] = element };
    }

    private static Dictionary<string, JsonElement> CreateCommandArgument(string command)
    {
        var element = JsonSerializer.Deserialize<JsonElement>($"\"{command}\"");
        return new Dictionary<string, JsonElement> { ["command"] = element };
    }

    [Fact]
    public async Task InvokeAsync_AutoMode_FileDelete_ShouldRejectWithMoveHint()
    {
        var sut = new DeleteProtectionMiddleware(FileDeleteOnlyDetectors);
        var context = CreateContext(FileToolNameConstants.FileDelete, PermissionMode.Auto,
            CreateFilePathArgument("src/test.cs"));

        await sut.InvokeAsync(context, CreateNext(), CancellationToken.None).ConfigureAwait(true);

        context.Result.Should().NotBeNull();
        context.Result!.IsApproved.Should().BeFalse("Auto 模式下文件删除应被拒绝");
        context.Result!.Reason.Should().Contain(".xxx", "拒绝消息应引导使用 .xxx 目录");
        context.Result!.Reason.Should().Contain("Move-Item", "拒绝消息应包含 move 命令");
    }

    [Fact]
    public async Task InvokeAsync_DefaultMode_FileDelete_ShouldRejectWithMoveHint()
    {
        var sut = new DeleteProtectionMiddleware(FileDeleteOnlyDetectors);
        var context = CreateContext(FileToolNameConstants.FileDelete, PermissionMode.Default,
            CreateFilePathArgument("src/test.cs"));

        await sut.InvokeAsync(context, CreateNext(), CancellationToken.None).ConfigureAwait(true);

        context.Result.Should().NotBeNull();
        context.Result!.IsApproved.Should().BeFalse("Default 模式下文件删除应被拒绝");
        context.Result!.Reason.Should().Contain(".xxx");
    }

    [Fact]
    public async Task InvokeAsync_AskMode_FileDelete_ShouldPendingConfirmation()
    {
        var sut = new DeleteProtectionMiddleware(FileDeleteOnlyDetectors);
        var context = CreateContext(FileToolNameConstants.FileDelete, PermissionMode.Ask,
            CreateFilePathArgument("src/test.cs"));

        await sut.InvokeAsync(context, CreateNext(), CancellationToken.None).ConfigureAwait(true);

        context.Result.Should().NotBeNull();
        context.Result!.IsApproved.Should().BeFalse();
        context.Result!.ConfirmationRequired.Should().BeTrue("Ask 模式应返回待确认");
        context.Result!.Reason.Should().Contain(".xxx", "确认消息应建议移动到 .xxx 目录");
    }

    [Fact]
    public async Task InvokeAsync_PlanMode_FileDelete_ShouldReject()
    {
        var sut = new DeleteProtectionMiddleware(FileDeleteOnlyDetectors);
        var context = CreateContext(FileToolNameConstants.FileDelete, PermissionMode.Plan,
            CreateFilePathArgument("src/test.cs"));

        await sut.InvokeAsync(context, CreateNext(), CancellationToken.None).ConfigureAwait(true);

        context.Result.Should().NotBeNull();
        context.Result!.IsApproved.Should().BeFalse("Plan 模式下文件删除应被拒绝");
        context.Result!.Reason.Should().Contain(".xxx");
    }

    [Fact]
    public async Task InvokeAsync_NonDeleteTool_ShouldPassThrough()
    {
        var sut = new DeleteProtectionMiddleware(FileDeleteOnlyDetectors);
        var nextCalled = false;
        var next = new MiddlewareDelegate<PermissionCheckContext>((ctx, ct) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateContext(FileToolNameConstants.FileRead, PermissionMode.Auto);

        await sut.InvokeAsync(context, next, CancellationToken.None).ConfigureAwait(true);

        nextCalled.Should().BeTrue("非删除工具应放行到下游中间件");
        context.Result.Should().BeNull("非删除工具不应设置结果");
    }

    [Fact]
    public async Task InvokeAsync_BypassMode_FileDelete_ShouldPassThrough()
    {
        var sut = new DeleteProtectionMiddleware(FileDeleteOnlyDetectors);
        var nextCalled = false;
        var next = new MiddlewareDelegate<PermissionCheckContext>((ctx, ct) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateContext(FileToolNameConstants.FileDelete, PermissionMode.BypassPermissions);

        await sut.InvokeAsync(context, next, CancellationToken.None).ConfigureAwait(true);

        nextCalled.Should().BeTrue("Bypass 模式下删除工具应放行");
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_AutoMode_NoFilePath_ShouldRejectWithGenericHint()
    {
        var sut = new DeleteProtectionMiddleware(FileDeleteOnlyDetectors);
        var context = CreateContext(FileToolNameConstants.FileDelete, PermissionMode.Auto);

        await sut.InvokeAsync(context, CreateNext(), CancellationToken.None).ConfigureAwait(true);

        context.Result.Should().NotBeNull();
        context.Result!.IsApproved.Should().BeFalse();
        context.Result!.Reason.Should().Contain(".xxx", "无路径参数时也应包含 .xxx 引导");
    }

    [Fact]
    public async Task InvokeAsync_AutoMode_TrashPath_ShouldContainTimestamp()
    {
        var sut = new DeleteProtectionMiddleware(FileDeleteOnlyDetectors);
        var context = CreateContext(FileToolNameConstants.FileDelete, PermissionMode.Auto,
            CreateFilePathArgument("src/Program.cs"));

        await sut.InvokeAsync(context, CreateNext(), CancellationToken.None).ConfigureAwait(true);

        context.Result!.Reason.Should().Contain(".del", "回收路径应以 .del 结尾");
        context.Result!.Reason.Should().Contain("Program", "回收路径应包含原文件名");
    }

    [Fact]
    public async Task InvokeAsync_AutoMode_ShellRm_ShouldRejectWithMoveHint()
    {
        var detectors = new IDeleteOperationDetector[] { new ShellDeleteDetector() };
        var sut = new DeleteProtectionMiddleware(detectors);
        var context = CreateContext(ShellToolNameConstants.ShellExecute, PermissionMode.Auto,
            CreateCommandArgument("rm src/test.cs"));

        await sut.InvokeAsync(context, CreateNext(), CancellationToken.None).ConfigureAwait(true);

        context.Result.Should().NotBeNull();
        context.Result!.IsApproved.Should().BeFalse("Auto 模式下 Shell rm 应被拒绝");
        context.Result!.Reason.Should().Contain(".xxx", "拒绝消息应引导使用 .xxx 目录");
    }

    [Fact]
    public async Task InvokeAsync_AutoMode_ShellNonDelete_ShouldPassThrough()
    {
        var detectors = new IDeleteOperationDetector[] { new ShellDeleteDetector() };
        var sut = new DeleteProtectionMiddleware(detectors);
        var nextCalled = false;
        var next = new MiddlewareDelegate<PermissionCheckContext>((ctx, ct) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateContext(ShellToolNameConstants.ShellExecute, PermissionMode.Auto,
            CreateCommandArgument("dotnet build"));

        await sut.InvokeAsync(context, next, CancellationToken.None).ConfigureAwait(true);

        nextCalled.Should().BeTrue("非删除 Shell 命令应放行");
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_NoDetectors_ShouldPassThrough()
    {
        var sut = new DeleteProtectionMiddleware([]);
        var nextCalled = false;
        var next = new MiddlewareDelegate<PermissionCheckContext>((ctx, ct) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateContext(FileToolNameConstants.FileDelete, PermissionMode.Auto,
            CreateFilePathArgument("src/test.cs"));

        await sut.InvokeAsync(context, next, CancellationToken.None).ConfigureAwait(true);

        nextCalled.Should().BeTrue("无检测器时应放行所有操作");
        context.Result.Should().BeNull();
    }
}
