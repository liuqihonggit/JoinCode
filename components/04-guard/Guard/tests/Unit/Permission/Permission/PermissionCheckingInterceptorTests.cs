
namespace Core.Tests.Permission;

public class PermissionCheckingInterceptorTests : IDisposable
{
    private readonly Mock<IToolPermissionManager> _mockPermissionManager;
    private readonly PermissionCheckingInterceptor _interceptor;

    public PermissionCheckingInterceptorTests()
    {
        _mockPermissionManager = new Mock<IToolPermissionManager>();
        _interceptor = new PermissionCheckingInterceptor(
            _mockPermissionManager.Object,
            NullLogger<PermissionCheckingInterceptor>.Instance);
    }

    public void Dispose()
    {
        _interceptor.Dispose();
    }

    #region OnBeforeToolInvokeAsync - Normal Flow Tests

    [Fact]
    public async Task OnBeforeToolInvokeAsync_GrantedPermission_ShouldReturnAllowed()
    {
        _mockPermissionManager
            .Setup(m => m.CheckPermissionAsync(It.IsAny<PermissionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PermissionResult.Granted());

        var context = new ToolInvokeContext(FileToolNameConstants.FileRead);

        var result = await _interceptor.OnBeforeToolInvokeAsync(context).ConfigureAwait(true);

        result.IsAllowed.Should().BeTrue();
        result.IsDenied.Should().BeFalse();
        result.RequiresConfirmation.Should().BeFalse();
    }

    [Fact]
    public async Task OnBeforeToolInvokeAsync_DeniedPermission_ShouldReturnDenied()
    {
        _mockPermissionManager
            .Setup(m => m.CheckPermissionAsync(It.IsAny<PermissionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PermissionResult.Denied("Permission denied for security reasons"));

        var context = new ToolInvokeContext("file_delete");

        var result = await _interceptor.OnBeforeToolInvokeAsync(context).ConfigureAwait(true);

        result.IsAllowed.Should().BeFalse();
        result.IsDenied.Should().BeTrue();
        result.RequiresConfirmation.Should().BeFalse();
        result.DenyReason.Should().Be("Permission denied for security reasons");
    }

    [Fact]
    public async Task OnBeforeToolInvokeAsync_PendingConfirmation_ShouldReturnConfirmationRequired()
    {
        _mockPermissionManager
            .Setup(m => m.CheckPermissionAsync(It.IsAny<PermissionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PermissionResult.PendingConfirmation("This operation requires user confirmation"));

        var context = new ToolInvokeContext(FileToolNameConstants.FileWrite);

        var result = await _interceptor.OnBeforeToolInvokeAsync(context).ConfigureAwait(true);

        result.IsAllowed.Should().BeFalse();
        result.IsDenied.Should().BeFalse();
        result.RequiresConfirmation.Should().BeTrue();
        result.ConfirmationPrompt.Should().Be("This operation requires user confirmation");
    }

    [Fact]
    public async Task OnBeforeToolInvokeAsync_WithArguments_ShouldPassArgumentsToManager()
    {
        PermissionRequest? capturedRequest = null;
        _mockPermissionManager
            .Setup(m => m.CheckPermissionAsync(It.IsAny<PermissionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PermissionRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(PermissionResult.Granted());

        var arguments = new Dictionary<string, JsonElement>
        {
            ["path"] = JsonSerializer.SerializeToElement("C:\\test.txt"),
            ["content"] = JsonSerializer.SerializeToElement("test content")
        };
        var context = new ToolInvokeContext(FileToolNameConstants.FileWrite, arguments);

        await _interceptor.OnBeforeToolInvokeAsync(context).ConfigureAwait(true);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.ToolName.Should().Be(FileToolNameConstants.FileWrite);
        capturedRequest.Arguments.Should().NotBeNull();
        capturedRequest.Arguments!.Count.Should().Be(2);
    }

    [Fact]
    public async Task OnBeforeToolInvokeAsync_ExpiredPermission_ShouldReturnDenied()
    {
        _mockPermissionManager
            .Setup(m => m.CheckPermissionAsync(It.IsAny<PermissionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PermissionResult.TemporaryGrant(TimeSpan.FromMilliseconds(-1)));

        var context = new ToolInvokeContext("expired_tool");

        var result = await _interceptor.OnBeforeToolInvokeAsync(context).ConfigureAwait(true);

        result.IsAllowed.Should().BeFalse();
        result.IsDenied.Should().BeTrue();
    }

    #endregion

    #region CheckPermissionOrThrowAsync Tests

    [Fact]
    public async Task CheckPermissionOrThrowAsync_GrantedPermission_ShouldNotThrow()
    {
        _mockPermissionManager
            .Setup(m => m.CheckPermissionAsync(It.IsAny<PermissionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PermissionResult.Granted());

        var context = new ToolInvokeContext(FileToolNameConstants.FileRead);

        var act = async () => await _interceptor.CheckPermissionOrThrowAsync(context).ConfigureAwait(true);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task CheckPermissionOrThrowAsync_DeniedPermission_ShouldThrowPermissionDeniedException()
    {
        _mockPermissionManager
            .Setup(m => m.CheckPermissionAsync(It.IsAny<PermissionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PermissionResult.Denied("Access denied"));

        var context = new ToolInvokeContext("restricted_tool");

        var act = async () => await _interceptor.CheckPermissionOrThrowAsync(context).ConfigureAwait(true);

        var exception = await act.Should().ThrowAsync<PermissionDeniedException>().ConfigureAwait(true);
        exception.Which.ResourceName.Should().Be("restricted_tool");
        exception.Which.DenyReason.Should().Be("Access denied");
    }

    [Fact]
    public async Task CheckPermissionOrThrowAsync_PendingConfirmation_ShouldThrowPermissionPendingConfirmationException()
    {
        _mockPermissionManager
            .Setup(m => m.CheckPermissionAsync(It.IsAny<PermissionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PermissionResult.PendingConfirmation("Please confirm this action"));

        var context = new ToolInvokeContext("confirm_required_tool");

        var act = async () => await _interceptor.CheckPermissionOrThrowAsync(context).ConfigureAwait(true);

        var exception = await act.Should().ThrowAsync<PermissionPendingConfirmationException>().ConfigureAwait(true);
        exception.Which.ToolName.Should().Be("confirm_required_tool");
        exception.Which.ConfirmationPrompt.Should().Be("Please confirm this action");
    }

    #endregion

    #region Priority Tests

    [Fact]
    public void Priority_ShouldReturn200()
    {
        _interceptor.Priority.Should().Be(200);
    }

    [Fact]
    public void Priority_ShouldBeHigherThanFileLockInterceptor()
    {
        _interceptor.Priority.Should().BeGreaterThan(100);
    }

    #endregion

    #region OnAfterToolInvokeAsync Tests

    [Fact]
    public async Task OnAfterToolInvokeAsync_SuccessfulExecution_ShouldLogDebug()
    {
        var context = new ToolInvokeContext("test_tool");
        var result = OperationResult<object?>.Ok(new { data = "test" });

        var act = async () => await _interceptor.OnAfterToolInvokeAsync(context, result).ConfigureAwait(true);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task OnAfterToolInvokeAsync_FailedExecution_ShouldLogWarning()
    {
        var context = new ToolInvokeContext("failing_tool");
        var result = OperationResult<object?>.Fail("Something went wrong");

        var act = async () => await _interceptor.OnAfterToolInvokeAsync(context, result).ConfigureAwait(true);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task OnAfterToolInvokeAsync_NullData_ShouldNotThrow()
    {
        var context = new ToolInvokeContext("test_tool");
        var result = OperationResult<object?>.Ok(null);

        var act = async () => await _interceptor.OnAfterToolInvokeAsync(context, result).ConfigureAwait(true);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    #endregion

    #region Exception Handling Tests

    [Fact]
    public async Task OnBeforeToolInvokeAsync_PermissionDeniedException_ShouldPropagate()
    {
        _mockPermissionManager
            .Setup(m => m.CheckPermissionAsync(It.IsAny<PermissionRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(PermissionDeniedException.ToolDenied("test_tool", "Denied"));

        var context = new ToolInvokeContext("test_tool");

        var act = async () => await _interceptor.OnBeforeToolInvokeAsync(context).ConfigureAwait(true);

        await act.Should().ThrowAsync<PermissionDeniedException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task OnBeforeToolInvokeAsync_OperationCanceledException_ShouldPropagate()
    {
        _mockPermissionManager
            .Setup(m => m.CheckPermissionAsync(It.IsAny<PermissionRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var context = new ToolInvokeContext("test_tool");

        var act = async () => await _interceptor.OnBeforeToolInvokeAsync(context).ConfigureAwait(true);

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task OnBeforeToolInvokeAsync_GenericException_ShouldReturnDenied()
    {
        _mockPermissionManager
            .Setup(m => m.CheckPermissionAsync(It.IsAny<PermissionRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Unexpected error"));

        var context = new ToolInvokeContext("test_tool");

        var result = await _interceptor.OnBeforeToolInvokeAsync(context).ConfigureAwait(true);

        result.IsDenied.Should().BeTrue();
        result.DenyReason.Should().Contain("权限检查失败");
        result.DenyReason.Should().Contain("Unexpected error");
    }

    [Fact]
    public async Task OnBeforeToolInvokeAsync_CancellationToken_ShouldPassToManager()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken capturedToken = default;

        _mockPermissionManager
            .Setup(m => m.CheckPermissionAsync(It.IsAny<PermissionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PermissionRequest, CancellationToken>((_, ct) => capturedToken = ct)
            .ReturnsAsync(PermissionResult.Granted());

        var context = new ToolInvokeContext("test_tool");
        await _interceptor.OnBeforeToolInvokeAsync(context, cts.Token).ConfigureAwait(true);

        capturedToken.Should().Be(cts.Token);
    }

    #endregion

    #region ToolInvokeContext Tests

    [Fact]
    public void ToolInvokeContext_Constructor_ShouldGenerateRequestId()
    {
        var context = new ToolInvokeContext("test_tool");

        context.ToolName.Should().Be("test_tool");
        context.RequestId.Should().NotBeNullOrEmpty();
        Guid.TryParse(context.RequestId, out _).Should().BeTrue();
        context.InvokeTime.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ToolInvokeContext_ConstructorWithRequestId_ShouldUseProvidedId()
    {
        var customId = Guid.NewGuid().ToString("N");
        var context = new ToolInvokeContext("test_tool", null, customId);

        context.RequestId.Should().Be(customId);
    }

    [Fact]
    public void ToolInvokeContext_NullToolName_ShouldThrowArgumentNullException()
    {
        var act = () => new ToolInvokeContext(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("toolName");
    }

    [Fact]
    public void ToolInvokeContext_NullRequestId_ShouldThrowArgumentNullException()
    {
        var act = () => new ToolInvokeContext("test_tool", null, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("requestId");
    }

    [Fact]
    public void ToolInvokeContext_WithArguments_ShouldStoreArguments()
    {
        var arguments = new Dictionary<string, JsonElement>
        {
            ["key1"] = JsonSerializer.SerializeToElement("value1"),
            ["key2"] = JsonSerializer.SerializeToElement(123)
        };
        var context = new ToolInvokeContext("test_tool", arguments);

        context.Arguments.Should().NotBeNull();
        context.Arguments!.Count.Should().Be(2);
    }

    #endregion

    #region PermissionInterceptResult Tests

    [Fact]
    public void PermissionInterceptResult_Allowed_ShouldBeAllowed()
    {
        var result = PermissionInterceptResult.Allowed();

        result.IsAllowed.Should().BeTrue();
        result.IsDenied.Should().BeFalse();
        result.RequiresConfirmation.Should().BeFalse();
    }

    [Fact]
    public void PermissionInterceptResult_Denied_ShouldBeDenied()
    {
        var result = PermissionInterceptResult.Denied("Access forbidden");

        result.IsAllowed.Should().BeFalse();
        result.IsDenied.Should().BeTrue();
        result.RequiresConfirmation.Should().BeFalse();
        result.DenyReason.Should().Be("Access forbidden");
    }

    [Fact]
    public void PermissionInterceptResult_ConfirmationRequired_ShouldRequireConfirmation()
    {
        var result = PermissionInterceptResult.ConfirmationRequired("Are you sure?");

        result.IsAllowed.Should().BeFalse();
        result.IsDenied.Should().BeFalse();
        result.RequiresConfirmation.Should().BeTrue();
        result.ConfirmationPrompt.Should().Be("Are you sure?");
    }

    #endregion

    #region PermissionPendingConfirmationException Tests

    [Fact]
    public void PermissionPendingConfirmationException_Constructor_ShouldSetProperties()
    {
        var exception = new PermissionPendingConfirmationException(
            "test_tool",
            "Please confirm this action",
            "request-123");

        exception.ToolName.Should().Be("test_tool");
        exception.ConfirmationPrompt.Should().Be("Please confirm this action");
        exception.RequestId.Should().Be("request-123");
        exception.Message.Should().Contain("test_tool");
        exception.Message.Should().Contain("Please confirm this action");
    }

    [Fact]
    public void PermissionPendingConfirmationException_NullToolName_ShouldThrowArgumentNullException()
    {
        var act = () => new PermissionPendingConfirmationException(null!, "prompt");
        act.Should().Throw<ArgumentNullException>().WithParameterName("toolName");
    }

    [Fact]
    public void PermissionPendingConfirmationException_NullPrompt_ShouldThrowArgumentNullException()
    {
        var act = () => new PermissionPendingConfirmationException("tool", null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("confirmationPrompt");
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_NullPermissionManager_ShouldThrowArgumentNullException()
    {
        var act = () => new PermissionCheckingInterceptor(null!, NullLogger<PermissionCheckingInterceptor>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("permissionManager");
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldNotThrow()
    {
        var act = () => new PermissionCheckingInterceptor(_mockPermissionManager.Object, null);
        act.Should().NotThrow();
    }

    #endregion

    #region Disposal Tests

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        _interceptor.Dispose();

        var act = () => _interceptor.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task OnBeforeToolInvokeAsync_AfterDispose_ShouldThrowObjectDisposedException()
    {
        _interceptor.Dispose();

        var context = new ToolInvokeContext("test_tool");
        var act = async () => await _interceptor.OnBeforeToolInvokeAsync(context).ConfigureAwait(true);

        await act.Should().ThrowAsync<ObjectDisposedException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task OnAfterToolInvokeAsync_AfterDispose_ShouldThrowObjectDisposedException()
    {
        _interceptor.Dispose();

        var context = new ToolInvokeContext("test_tool");
        var result = OperationResult<object?>.Ok(null);
        var act = async () => await _interceptor.OnAfterToolInvokeAsync(context, result).ConfigureAwait(true);

        await act.Should().ThrowAsync<ObjectDisposedException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task CheckPermissionOrThrowAsync_AfterDispose_ShouldThrowObjectDisposedException()
    {
        _interceptor.Dispose();

        var context = new ToolInvokeContext("test_tool");
        var act = async () => await _interceptor.CheckPermissionOrThrowAsync(context).ConfigureAwait(true);

        await act.Should().ThrowAsync<ObjectDisposedException>().ConfigureAwait(true);
    }

    #endregion

    #region Integration with PermissionRequest

    [Fact]
    public async Task OnBeforeToolInvokeAsync_ShouldCreateCorrectPermissionRequest()
    {
        PermissionRequest? capturedRequest = null;
        _mockPermissionManager
            .Setup(m => m.CheckPermissionAsync(It.IsAny<PermissionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PermissionRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(PermissionResult.Granted());

        var arguments = new Dictionary<string, JsonElement>
        {
            ["path"] = JsonSerializer.SerializeToElement("test.txt")
        };
        var context = new ToolInvokeContext(FileToolNameConstants.FileRead, arguments, "custom-request-id");

        await _interceptor.OnBeforeToolInvokeAsync(context).ConfigureAwait(true);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.ToolName.Should().Be(FileToolNameConstants.FileRead);
        capturedRequest.Arguments.Should().NotBeNull();
        capturedRequest.RequestId.Should().Be("custom-request-id");
    }

    #endregion
}
