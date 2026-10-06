namespace Core.Tests.Permission;

/// <summary>
/// PermissionCheckingInterceptor 确定性测试 — mock IToolPermissionManager / IToolPermissionFilter 消除依赖
/// <para>测试拦截入口逻辑:filter 拒绝、权限批准/确认/拒绝、异常处理。不依赖时序。</para>
/// </summary>
public sealed class PermissionCheckingInterceptorTest {

    #region 辅助构造

    private static ToolInvokeContext CreateContext(string toolName = "Bash") {
        return new ToolInvokeContext(toolName);
    }

    private static PermissionCheckingInterceptor CreateInterceptor(
        Mock<IToolPermissionManager>? managerMock = null,
        IToolPermissionFilter? filter = null) {
        managerMock ??= new Mock<IToolPermissionManager>();
        return new PermissionCheckingInterceptor(
            managerMock.Object,
            logger: null,
            toolPermissionFilter: filter);
    }

    private static Mock<IToolPermissionManager> CreateManagerMock(PermissionResult result) {
        var mock = new Mock<IToolPermissionManager>();
        mock.Setup(m => m.CheckPermissionAsync(It.IsAny<PermissionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock;
    }

    private static OperationResult<object?> OkResult() => new() { Success = true, Data = null };
    private static OperationResult<object?> FailResult(string error) => new() { Success = false, ErrorMessage = error };

    #endregion

    #region OnBeforeToolInvokeAsync — 拦截入口

    [Fact]
    public async Task OnBeforeToolInvokeAsync_无filter_直接检查权限() {
        var managerMock = CreateManagerMock(PermissionResult.Granted());
        await using var interceptor = CreateInterceptor(managerMock, filter: null);

        var result = await interceptor.OnBeforeToolInvokeAsync(CreateContext());

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task OnBeforeToolInvokeAsync_filter拒绝_返回Denied() {
        var managerMock = CreateManagerMock(PermissionResult.Granted());
        var filterMock = new Mock<IToolPermissionFilter>();
        filterMock.Setup(f => f.IsToolDenied(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(true);

        await using var interceptor = new PermissionCheckingInterceptor(
            managerMock.Object,
            logger: null,
            toolPermissionFilter: filterMock.Object);

        var result = await interceptor.OnBeforeToolInvokeAsync(CreateContext("DangerousTool"));

        result.IsDenied.Should().BeTrue();
        result.DenyReason.Should().Contain("DangerousTool").And.Contain("拒绝");
    }

    [Fact]
    public async Task OnBeforeToolInvokeAsync_filter未拒绝_继续检查权限() {
        var managerMock = CreateManagerMock(PermissionResult.Granted());
        var filterMock = new Mock<IToolPermissionFilter>();
        filterMock.Setup(f => f.IsToolDenied(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(false);

        await using var interceptor = new PermissionCheckingInterceptor(
            managerMock.Object,
            logger: null,
            toolPermissionFilter: filterMock.Object);

        var result = await interceptor.OnBeforeToolInvokeAsync(CreateContext());

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task OnBeforeToolInvokeAsync_权限批准_返回Allowed() {
        var managerMock = CreateManagerMock(PermissionResult.Granted());
        await using var interceptor = CreateInterceptor(managerMock);

        var result = await interceptor.OnBeforeToolInvokeAsync(CreateContext());

        result.IsAllowed.Should().BeTrue();
        result.IsDenied.Should().BeFalse();
        result.RequiresConfirmation.Should().BeFalse();
    }

    [Fact]
    public async Task OnBeforeToolInvokeAsync_权限需确认_返回ConfirmationRequired() {
        var managerMock = CreateManagerMock(PermissionResult.PendingConfirmation("请确认执行"));
        await using var interceptor = CreateInterceptor(managerMock);

        var result = await interceptor.OnBeforeToolInvokeAsync(CreateContext());

        result.RequiresConfirmation.Should().BeTrue();
        result.ConfirmationPrompt.Should().Be("请确认执行");
    }

    [Fact]
    public async Task OnBeforeToolInvokeAsync_权限拒绝_返回Denied() {
        var managerMock = CreateManagerMock(PermissionResult.Denied("危险操作"));
        await using var interceptor = CreateInterceptor(managerMock);

        var result = await interceptor.OnBeforeToolInvokeAsync(CreateContext());

        result.IsDenied.Should().BeTrue();
        result.DenyReason.Should().Be("危险操作");
    }

    [Fact]
    public async Task OnBeforeToolInvokeAsync_权限拒绝带默认原因_返回拒绝原因() {
        var managerMock = CreateManagerMock(PermissionResult.Denied("权限被拒绝"));
        await using var interceptor = CreateInterceptor(managerMock);

        var result = await interceptor.OnBeforeToolInvokeAsync(CreateContext());

        result.IsDenied.Should().BeTrue();
        result.DenyReason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task OnBeforeToolInvokeAsync_临时授权未过期_返回Allowed() {
        var managerMock = CreateManagerMock(PermissionResult.TemporaryGrant(TimeSpan.FromHours(1)));
        await using var interceptor = CreateInterceptor(managerMock);

        var result = await interceptor.OnBeforeToolInvokeAsync(CreateContext());

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task OnBeforeToolInvokeAsync_manager抛异常_返回Denied() {
        var managerMock = new Mock<IToolPermissionManager>();
        managerMock.Setup(m => m.CheckPermissionAsync(It.IsAny<PermissionRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("manager boom"));

        await using var interceptor = CreateInterceptor(managerMock);

        var result = await interceptor.OnBeforeToolInvokeAsync(CreateContext());

        result.IsDenied.Should().BeTrue();
        result.DenyReason.Should().Contain("权限检查失败");
    }

    [Fact]
    public async Task OnBeforeToolInvokeAsync_取消令牌_重抛OperationCanceledException() {
        var managerMock = new Mock<IToolPermissionManager>();
        managerMock.Setup(m => m.CheckPermissionAsync(It.IsAny<PermissionRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await using var interceptor = CreateInterceptor(managerMock);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var act = () => interceptor.OnBeforeToolInvokeAsync(CreateContext(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task OnBeforeToolInvokeAsync_Dispose后_抛ObjectDisposedException() {
        var interceptor = CreateInterceptor();
        interceptor.Dispose();

        var act = () => interceptor.OnBeforeToolInvokeAsync(CreateContext());
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    #endregion

    #region OnAfterToolInvokeAsync

    [Fact]
    public async Task OnAfterToolInvokeAsync_成功_返回CompletedTask() {
        await using var interceptor = CreateInterceptor();
        var context = CreateContext();

        var act = () => interceptor.OnAfterToolInvokeAsync(context, OkResult());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OnAfterToolInvokeAsync_失败_返回CompletedTask() {
        await using var interceptor = CreateInterceptor();
        var context = CreateContext();

        var act = () => interceptor.OnAfterToolInvokeAsync(context, FailResult("error message"));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OnAfterToolInvokeAsync_Dispose后_抛ObjectDisposedException() {
        var interceptor = CreateInterceptor();
        interceptor.Dispose();

        var context = CreateContext();
        var act = () => interceptor.OnAfterToolInvokeAsync(context, OkResult());
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    #endregion

    #region CheckPermissionAsync — 决策结果

    [Fact]
    public async Task CheckPermissionAsync_批准_返回Allowed() {
        var managerMock = CreateManagerMock(PermissionResult.Granted());
        await using var interceptor = CreateInterceptor(managerMock);

        var outcome = await interceptor.CheckPermissionAsync(CreateContext());

        outcome.Decision.Should().Be(JoinCode.Abstractions.Security.PermissionDecision.Allowed);
    }

    [Fact]
    public async Task CheckPermissionAsync_需确认_返回Pending() {
        var managerMock = CreateManagerMock(PermissionResult.PendingConfirmation("confirm?"));
        await using var interceptor = CreateInterceptor(managerMock);

        var outcome = await interceptor.CheckPermissionAsync(CreateContext());

        outcome.Decision.Should().Be(JoinCode.Abstractions.Security.PermissionDecision.PendingConfirmation);
        outcome.ConfirmationPrompt.Should().Be("confirm?");
    }

    [Fact]
    public async Task CheckPermissionAsync_拒绝_返回Denied() {
        var managerMock = CreateManagerMock(PermissionResult.Denied("no"));
        await using var interceptor = CreateInterceptor(managerMock);

        var outcome = await interceptor.CheckPermissionAsync(CreateContext());

        outcome.Decision.Should().Be(JoinCode.Abstractions.Security.PermissionDecision.Denied);
        outcome.DenyReason.Should().Be("no");
    }

    #endregion

    #region Priority

    [Fact]
    public async Task Priority_应为200() {
        await using var interceptor = CreateInterceptor();
        interceptor.Priority.Should().Be(200);
    }

    #endregion

    #region Dispose

    [Fact]
    public void Dispose_多次调用_不抛异常() {
        var interceptor = CreateInterceptor();
        interceptor.Dispose();
        var act = () => interceptor.Dispose();
        act.Should().NotThrow();
    }

    #endregion
}
