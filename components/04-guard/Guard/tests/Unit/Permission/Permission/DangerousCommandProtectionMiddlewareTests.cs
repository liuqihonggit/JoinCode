
namespace Guard.Tests.Permission.Permission;

/// <summary>
/// DangerousCommandProtectionMiddleware 单元测试 — 验证危险命令保护机制
/// 覆盖: 删除保护、Shell 危险命令拦截、各风险处理器
/// </summary>
public sealed class DangerousCommandProtectionMiddlewareTests
{
    private static readonly IDeleteOperationDetector[] FileDeleteOnlyDetectors = [new FileDeleteDetector()];

    private static readonly IDeleteOperationDetector[] ShellDeleteDetectors = [new ShellDeleteDetector()];

    private static readonly ICommandRiskHandler[] AllRiskHandlers =
    [
        new FileDeletionRiskHandler(),
        new DirectoryDeletionRiskHandler(),
        new SystemModificationRiskHandler(),
        new PrivilegeEscalationRiskHandler(),
        new RemoteExecutionRiskHandler(),
        new DataModificationRiskHandler(),
        new RecursiveOperationRiskHandler(),
        new ForceOperationRiskHandler()
    ];

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

    // === file_delete 工具测试 ===

    [Fact]
    public async Task InvokeAsync_AutoMode_FileDelete_ShouldRejectWithMoveHint()
    {
        var sut = new DangerousCommandProtectionMiddleware(AllRiskHandlers, deleteDetectors: FileDeleteOnlyDetectors);
        var context = CreateContext(FileToolNameConstants.FileDelete, PermissionMode.Auto,
            CreateFilePathArgument("src/test.cs"));

        await sut.InvokeAsync(context, CreateNext(), CancellationToken.None).ConfigureAwait(true);

        context.Result.Should().NotBeNull();
        context.Result!.IsApproved.Should().BeFalse();
        context.Result!.Reason.Should().Contain(".xxx");
        context.Result!.Reason.Should().Contain("Move-Item");
    }

    [Fact]
    public async Task InvokeAsync_AskMode_FileDelete_ShouldPendingConfirmation()
    {
        var sut = new DangerousCommandProtectionMiddleware(AllRiskHandlers, deleteDetectors: FileDeleteOnlyDetectors);
        var context = CreateContext(FileToolNameConstants.FileDelete, PermissionMode.Ask,
            CreateFilePathArgument("src/test.cs"));

        await sut.InvokeAsync(context, CreateNext(), CancellationToken.None).ConfigureAwait(true);

        context.Result.Should().NotBeNull();
        context.Result!.ConfirmationRequired.Should().BeTrue();
        context.Result!.Reason.Should().Contain(".xxx");
    }

    [Fact]
    public async Task InvokeAsync_PlanMode_FileDelete_ShouldReject()
    {
        var sut = new DangerousCommandProtectionMiddleware(AllRiskHandlers, deleteDetectors: FileDeleteOnlyDetectors);
        var context = CreateContext(FileToolNameConstants.FileDelete, PermissionMode.Plan,
            CreateFilePathArgument("src/test.cs"));

        await sut.InvokeAsync(context, CreateNext(), CancellationToken.None).ConfigureAwait(true);

        context.Result.Should().NotBeNull();
        context.Result!.IsApproved.Should().BeFalse();
        context.Result!.Reason.Should().Contain("Plan");
    }

    // === Shell 危险命令测试 ===

    [Fact]
    public async Task InvokeAsync_AutoMode_ShellRm_ShouldRejectWithMoveHint()
    {
        var detector = new DestructiveCommandDetector();
        var sut = new DangerousCommandProtectionMiddleware(AllRiskHandlers, detector, ShellDeleteDetectors);
        var context = CreateContext(ShellToolNameConstants.Bash, PermissionMode.Auto,
            CreateCommandArgument("rm src/test.cs"));

        await sut.InvokeAsync(context, CreateNext(), CancellationToken.None).ConfigureAwait(true);

        context.Result.Should().NotBeNull();
        context.Result!.IsApproved.Should().BeFalse();
        context.Result!.Reason.Should().Contain(".xxx");
    }

    [Fact]
    public async Task InvokeAsync_AutoMode_Sudo_ShouldRejectWithPrivilegeWarning()
    {
        var detector = new DestructiveCommandDetector();
        var sut = new DangerousCommandProtectionMiddleware(AllRiskHandlers, detector);
        var context = CreateContext(ShellToolNameConstants.Bash, PermissionMode.Auto,
            CreateCommandArgument("sudo apt install something"));

        await sut.InvokeAsync(context, CreateNext(), CancellationToken.None).ConfigureAwait(true);

        context.Result.Should().NotBeNull();
        context.Result!.IsApproved.Should().BeFalse();
        context.Result!.Reason.Should().Contain("权限提升");
    }

    [Fact]
    public async Task InvokeAsync_AutoMode_Curl_ShouldRejectWithRemoteWarning()
    {
        var detector = new DestructiveCommandDetector();
        var sut = new DangerousCommandProtectionMiddleware(AllRiskHandlers, detector);
        var context = CreateContext(ShellToolNameConstants.Bash, PermissionMode.Auto,
            CreateCommandArgument("curl https://example.com"));

        await sut.InvokeAsync(context, CreateNext(), CancellationToken.None).ConfigureAwait(true);

        context.Result.Should().NotBeNull();
        context.Result!.IsApproved.Should().BeFalse();
        context.Result!.Reason.Should().Contain("远程");
    }

    [Fact]
    public async Task InvokeAsync_AutoMode_Format_ShouldRejectWithSystemWarning()
    {
        var detector = new DestructiveCommandDetector();
        var sut = new DangerousCommandProtectionMiddleware(AllRiskHandlers, detector);
        var context = CreateContext(ShellToolNameConstants.Bash, PermissionMode.Auto,
            CreateCommandArgument("format c:"));

        await sut.InvokeAsync(context, CreateNext(), CancellationToken.None).ConfigureAwait(true);

        context.Result.Should().NotBeNull();
        context.Result!.IsApproved.Should().BeFalse();
        context.Result!.Reason.Should().Contain("系统修改");
    }

    // === 放行测试 ===

    [Fact]
    public async Task InvokeAsync_NonDeleteTool_ShouldPassThrough()
    {
        var sut = new DangerousCommandProtectionMiddleware(AllRiskHandlers, deleteDetectors: FileDeleteOnlyDetectors);
        var nextCalled = false;
        var next = new MiddlewareDelegate<PermissionCheckContext>((ctx, ct) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateContext(FileToolNameConstants.FileRead, PermissionMode.Auto);

        await sut.InvokeAsync(context, next, CancellationToken.None).ConfigureAwait(true);

        nextCalled.Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_BypassMode_ShouldPassThrough()
    {
        var sut = new DangerousCommandProtectionMiddleware(AllRiskHandlers, deleteDetectors: FileDeleteOnlyDetectors);
        var nextCalled = false;
        var next = new MiddlewareDelegate<PermissionCheckContext>((ctx, ct) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateContext(FileToolNameConstants.FileDelete, PermissionMode.BypassPermissions,
            CreateFilePathArgument("src/test.cs"));

        await sut.InvokeAsync(context, next, CancellationToken.None).ConfigureAwait(true);

        nextCalled.Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_SafeShellCommand_ShouldPassThrough()
    {
        var detector = new DestructiveCommandDetector();
        var sut = new DangerousCommandProtectionMiddleware(AllRiskHandlers, detector);
        var nextCalled = false;
        var next = new MiddlewareDelegate<PermissionCheckContext>((ctx, ct) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateContext(ShellToolNameConstants.Bash, PermissionMode.Auto,
            CreateCommandArgument("dotnet build"));

        await sut.InvokeAsync(context, next, CancellationToken.None).ConfigureAwait(true);

        nextCalled.Should().BeTrue();
        context.Result.Should().BeNull();
    }

    // === 无检测器/处理器时的降级 ===

    [Fact]
    public async Task InvokeAsync_NoHandlers_ShouldUseFallbackMessage()
    {
        var detector = new DestructiveCommandDetector();
        var sut = new DangerousCommandProtectionMiddleware([], detector);
        var context = CreateContext(ShellToolNameConstants.Bash, PermissionMode.Auto,
            CreateCommandArgument("rm src/test.cs"));

        await sut.InvokeAsync(context, CreateNext(), CancellationToken.None).ConfigureAwait(true);

        context.Result.Should().NotBeNull();
        context.Result!.IsApproved.Should().BeFalse();
        context.Result!.Reason.Should().Contain("已被阻止");
    }

    // === 风险优先级测试 ===

    [Fact]
    public async Task InvokeAsync_MultipleRisks_ShouldSelectHighestPriority()
    {
        var detector = new DestructiveCommandDetector();
        var sut = new DangerousCommandProtectionMiddleware(AllRiskHandlers, detector);
        // rm -rf / 包含 FileDeletion + RecursiveOperation + ForceOperation + PathEscape
        var context = CreateContext(ShellToolNameConstants.Bash, PermissionMode.Auto,
            CreateCommandArgument("rm -rf /"));

        await sut.InvokeAsync(context, CreateNext(), CancellationToken.None).ConfigureAwait(true);

        context.Result.Should().NotBeNull();
        context.Result!.IsApproved.Should().BeFalse();
        // FileDeletion 优先级最高，应触发 .xxx 引导
        context.Result!.Reason.Should().Contain(".xxx");
    }
}
