
namespace Core.Tests.Services;

public class ExceptionServiceTests {
    private readonly Mock<ILogger<ExceptionService>> _loggerMock;
    private readonly ExceptionService _exceptionService;

    public ExceptionServiceTests() {
        _loggerMock = new Mock<ILogger<ExceptionService>>();
        _exceptionService = new ExceptionService(_loggerMock.Object);
    }

    [Fact]
    public void HandleException_ConfigurationException_ShouldReturnConfigurationError() {
        // Arrange
        var exception = new ConfigurationException("Test config error");

        // Act
        var result = _exceptionService.HandleException(exception);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("配置错误", result.ErrorMessage);
        Assert.Contains("Test config error", result.ErrorMessage);
        Assert.Equal(global::JoinCode.Abstractions.Exceptions.ErrorCode.ConfigurationGeneral.ToValue(), result.ErrorType);
    }

    [Fact]
    public void HandleException_ApiException_ShouldReturnApiError() {
        // Arrange
        var exception = new ApiException("Test API error");

        // Act
        var result = _exceptionService.HandleException(exception);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("API 错误", result.ErrorMessage);
        Assert.Contains("Test API error", result.ErrorMessage);
        Assert.Equal(global::JoinCode.Abstractions.Exceptions.ErrorCode.ApiGeneral.ToValue(), result.ErrorType);
    }

    [Fact]
    public void HandleException_CodeExecutionException_ShouldReturnCodeExecutionError() {
        // Arrange
        var exception = new CodeExecutionException("Test code execution error");

        // Act
        var result = _exceptionService.HandleException(exception);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("代码执行错误", result.ErrorMessage);
        Assert.Contains("Test code execution error", result.ErrorMessage);
        Assert.Equal(global::JoinCode.Abstractions.Exceptions.ErrorCode.CodeExecutionGeneral.ToValue(), result.ErrorType);
    }

    [Fact]
    public void HandleException_PermissionDeniedException_ShouldReturnPermissionError() {
        // Arrange
        var exception = PermissionDeniedException.ToolDenied("test-tool", "Test deny reason");

        // Act
        var result = _exceptionService.HandleException(exception);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("权限错误", result.ErrorMessage);
        Assert.Equal(global::JoinCode.Abstractions.Exceptions.ErrorCode.PermissionToolDenied.ToValue(), result.ErrorType);
    }

    [Fact]
    public void HandleException_WorkflowException_ShouldReturnWorkflowError() {
        // Arrange
        var exception = new WorkflowException("Test workflow error");

        // Act
        var result = _exceptionService.HandleException(exception);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("工作流错误", result.ErrorMessage);
        Assert.Contains("Test workflow error", result.ErrorMessage);
        Assert.Equal(global::JoinCode.Abstractions.Exceptions.ErrorCode.WorkflowGeneral.ToValue(), result.ErrorType);
    }

    [Fact]
    public void HandleException_GeneralException_ShouldReturnUnexpectedError() {
        // Arrange
        var exception = new Exception("Test general error");

        // Act
        var result = _exceptionService.HandleException(exception);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("发生意外错误", result.ErrorMessage);
        Assert.Contains("Test general error", result.ErrorMessage);
        Assert.Equal(global::JoinCode.Abstractions.Exceptions.ErrorCode.General.ToValue(), result.ErrorType);
    }

    [Fact]
    public void HandleExceptionT_ConfigurationException_ShouldReturnConfigurationError() {
        // Arrange
        var exception = new ConfigurationException("Test config error");

        // Act
        var result = _exceptionService.HandleException<string>(exception);

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.Data);
        Assert.Contains("配置错误", result.ErrorMessage);
        Assert.Equal(global::JoinCode.Abstractions.Exceptions.ErrorCode.ConfigurationGeneral.ToValue(), result.ErrorType);
    }

    [Fact]
    public void HandleExceptionT_Success_ShouldReturnOkResult() {
        // Arrange & Act
        var result = OperationResult<string>.Ok("success");

        // Assert
        Assert.True(result.Success);
        Assert.Equal("success", result.Data);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ExecuteWithExceptionHandling_SuccessfulAction_ShouldReturnResult() {
        // Arrange
        var expectedResult = "success";

        // Act
        var result = _exceptionService.ExecuteWithExceptionHandling(() => expectedResult);

        // Assert
        Assert.Equal(expectedResult, result);
    }

    [Fact]
    public void ExecuteWithExceptionHandling_FailingAction_ShouldThrowWorkflowException() {
        // Arrange
        Func<string> failingAction = () => throw new Exception("Test error");

        // Act & Assert
        var exception = Assert.Throws<WorkflowException>(() =>
            _exceptionService.ExecuteWithExceptionHandling(failingAction));
        Assert.Contains("发生错误", exception.Message);
        Assert.Equal(global::JoinCode.Abstractions.Exceptions.ErrorCode.WorkflowExecution.ToValue(), exception.ErrorCode);
    }

    [Fact]
    public void ExecuteWithExceptionHandling_WithCustomMessage_ShouldUseCustomMessage() {
        // Arrange
        Func<string> failingAction = () => throw new Exception("Test error");
        var customMessage = "Custom error message";

        // Act & Assert
        var exception = Assert.Throws<WorkflowException>(() =>
            _exceptionService.ExecuteWithExceptionHandling(failingAction, customMessage));
        Assert.Contains(customMessage, exception.Message);
        Assert.Equal(global::JoinCode.Abstractions.Exceptions.ErrorCode.WorkflowExecution.ToValue(), exception.ErrorCode);
    }

    [Fact]
    public async Task ExecuteWithExceptionHandlingAsync_SuccessfulAction_ShouldReturnResult() {
        // Arrange
        var expectedResult = "success";

        // Act
        var result = await _exceptionService.ExecuteWithExceptionHandlingAsync(() => Task.FromResult(expectedResult)).ConfigureAwait(true);

        // Assert
        Assert.Equal(expectedResult, result);
    }

    [Fact]
    public async Task ExecuteWithExceptionHandlingAsync_FailingAction_ShouldThrowWorkflowException() {
        // Arrange
        Func<Task<string>> failingAction = () => throw new Exception("Test error");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<WorkflowException>(() =>
            _exceptionService.ExecuteWithExceptionHandlingAsync(failingAction)).ConfigureAwait(true);
        Assert.Contains("发生错误", exception.Message);
        Assert.Equal(global::JoinCode.Abstractions.Exceptions.ErrorCode.WorkflowExecution.ToValue(), exception.ErrorCode);
    }

    [Fact]
    public async Task ExecuteWithExceptionHandlingAsync_WithCustomMessage_ShouldUseCustomMessage() {
        // Arrange
        Func<Task<string>> failingAction = () => throw new Exception("Test error");
        var customMessage = "Custom async error message";

        // Act & Assert
        var exception = await Assert.ThrowsAsync<WorkflowException>(() =>
            _exceptionService.ExecuteWithExceptionHandlingAsync(failingAction, customMessage)).ConfigureAwait(true);
        Assert.Contains(customMessage, exception.Message);
        Assert.Equal(global::JoinCode.Abstractions.Exceptions.ErrorCode.WorkflowExecution.ToValue(), exception.ErrorCode);
    }

    [Fact]
    public void WorkflowException_ShouldHaveErrorCodeAndCategory() {
        // Arrange & Act
        var exception = new WorkflowException("Test error", "CUSTOM_ERROR", ErrorCategory.General);

        // Assert
        Assert.Equal("CUSTOM_ERROR", exception.ErrorCode);
        Assert.Equal(ErrorCategory.General, exception.Category);
        Assert.NotNull(exception.Context);
    }

    [Fact]
    public void WorkflowException_WithInnerException_ShouldPreserveInnerException() {
        // Arrange
        var innerException = new Exception("Inner error");

        // Act
        var exception = new WorkflowException("Test error", innerException);

        // Assert
        Assert.NotNull(exception.InnerException);
        Assert.Equal("Inner error", exception.InnerException.Message);
    }

    [Fact]
    public void WorkflowException_WithContext_ShouldAllowContextConfiguration() {
        // Arrange & Act
        var exception = new WorkflowException("Test error")
            .WithRequestId("req-123")
            .WithOperation("test-operation")
            .WithContext(ctx => ctx.WithData("key", "value"));

        // Assert
        Assert.Equal("req-123", exception.Context.RequestId);
        Assert.Equal("test-operation", exception.Context.OperationName);
        Assert.Equal("value", exception.Context.Data["key"].GetString());
    }

    [Fact]
    public void ConfigurationException_Missing_ShouldCreateMissingConfigException() {
        // Arrange & Act
        var exception = ConfigurationException.Missing("TestKey", "/config.json");

        // Assert
        Assert.Equal(global::JoinCode.Abstractions.Exceptions.ErrorCode.ConfigurationMissing.ToValue(), exception.ErrorCode);
        Assert.Equal("TestKey", exception.ConfigurationKey);
        Assert.Equal("/config.json", exception.ConfigurationFilePath);
        Assert.Contains("缺少必需的配置项", exception.Message);
    }

    [Fact]
    public void ConfigurationException_Invalid_ShouldCreateInvalidConfigException() {
        // Arrange & Act
        var exception = ConfigurationException.Invalid("TestKey", "invalid-value", "格式不正确");

        // Assert
        Assert.Equal(global::JoinCode.Abstractions.Exceptions.ErrorCode.ConfigurationInvalid.ToValue(), exception.ErrorCode);
        Assert.Equal("TestKey", exception.ConfigurationKey);
        Assert.Equal("invalid-value", exception.ConfigurationValue);
        Assert.Contains("格式不正确", exception.Message);
    }

    [Fact]
    public void ApiException_Connection_ShouldCreateConnectionException() {
        // Arrange & Act
        var exception = ApiException.Connection("https://api.example.com");

        // Assert
        Assert.Equal(global::JoinCode.Abstractions.Exceptions.ErrorCode.ApiConnection.ToValue(), exception.ErrorCode);
        Assert.Equal("https://api.example.com", exception.Endpoint);
        Assert.True(exception.IsRetryable);
        Assert.Equal(3, exception.SuggestedRetryCount);
    }

    [Fact]
    public void ApiException_Timeout_ShouldCreateTimeoutException() {
        // Arrange & Act
        var exception = ApiException.Timeout("https://api.example.com", TimeSpan.FromSeconds(30));

        // Assert
        Assert.Equal(global::JoinCode.Abstractions.Exceptions.ErrorCode.ApiTimeout.ToValue(), exception.ErrorCode);
        Assert.Equal("https://api.example.com", exception.Endpoint);
        Assert.True(exception.IsRetryable);
    }

    [Fact]
    public void ApiException_RateLimit_ShouldCreateRateLimitException() {
        // Arrange & Act
        var exception = ApiException.RateLimit("https://api.example.com", TimeSpan.FromSeconds(60));

        // Assert
        Assert.Equal(global::JoinCode.Abstractions.Exceptions.ErrorCode.ApiRateLimit.ToValue(), exception.ErrorCode);
        Assert.Equal(429, exception.StatusCode);
        Assert.True(exception.IsRetryable);
    }

    [Fact]
    public void ApiException_ResponseError_ShouldNotBeRetryableFor4xx() {
        // Arrange & Act
        var exception = ApiException.ResponseError("https://api.example.com", 400, "Bad Request");

        // Assert
        Assert.Equal(global::JoinCode.Abstractions.Exceptions.ErrorCode.ApiResponseError.ToValue(), exception.ErrorCode);
        Assert.Equal(400, exception.StatusCode);
        Assert.False(exception.IsRetryable);
        Assert.Null(exception.SuggestedRetryCount);
    }

    [Fact]
    public void CodeExecutionException_WithResult_ShouldStoreMetadata() {
        // Arrange
        var result = new CodeExecutionResult(
            CodeSnippet: "code",
            Language: "csharp",
            StandardError: "compiler error");

        // Act
        var exception = new CodeExecutionException(
            "代码编译失败 (csharp)",
            result,
            errorCode: global::JoinCode.Abstractions.Exceptions.ErrorCode.CodeExecutionCompilation.ToValue());

        // Assert
        Assert.Equal(global::JoinCode.Abstractions.Exceptions.ErrorCode.CodeExecutionCompilation.ToValue(), exception.ErrorCode);
        Assert.Equal("csharp", exception.Result?.Language);
        Assert.Equal("code", exception.Result?.CodeSnippet);
        Assert.Equal("compiler error", exception.Result?.StandardError);
    }

    [Fact]
    public void CodeExecutionException_WithExitCode_ShouldBeRetryable() {
        // Arrange & Act
        var result = new CodeExecutionResult(Language: "python", ExitCode: 1);
        var exception = new CodeExecutionException(
            "代码执行失败，退出代码: 1",
            result,
            errorCode: global::JoinCode.Abstractions.Exceptions.ErrorCode.CodeExecutionRuntime.ToValue());

        // Assert
        Assert.Equal(global::JoinCode.Abstractions.Exceptions.ErrorCode.CodeExecutionRuntime.ToValue(), exception.ErrorCode);
        Assert.Equal(1, exception.Result?.ExitCode);
        Assert.Equal("python", exception.Result?.Language);
        Assert.True(exception.IsRetryable);
    }

    [Fact]
    public void PermissionDeniedException_ToolDenied_ShouldCreateToolDeniedException() {
        // Arrange & Act
        var exception = PermissionDeniedException.ToolDenied("test-tool", "Not allowed", "test-agent");

        // Assert
        Assert.Equal(global::JoinCode.Abstractions.Exceptions.ErrorCode.PermissionToolDenied.ToValue(), exception.ErrorCode);
        Assert.Equal(PermissionResourceType.Tool, exception.ResourceType);
        Assert.Equal("test-tool", exception.ResourceName);
        Assert.Equal("test-agent", exception.Principal);
        Assert.Equal("Not allowed", exception.DenyReason);
        Assert.False(exception.IsRetryable);
    }

    [Fact]
    public void PermissionDeniedException_PathDenied_ShouldCreatePathDeniedException() {
        // Arrange & Act
        var exception = PermissionDeniedException.PathDenied("/secret/path", "Access denied");

        // Assert
        Assert.Equal(global::JoinCode.Abstractions.Exceptions.ErrorCode.PermissionPathDenied.ToValue(), exception.ErrorCode);
        Assert.Equal(PermissionResourceType.Path, exception.ResourceType);
        Assert.Equal("/secret/path", exception.ResourceName);
    }

    [Fact]
    public void WorkflowException_DefaultErrorCode_ShouldBeWorkflowGeneral() {
        // Arrange & Act
        var exception = new WorkflowException("Test error");

        // Assert
        Assert.Equal(global::JoinCode.Abstractions.Exceptions.ErrorCode.WorkflowGeneral.ToValue(), exception.ErrorCode);
        Assert.Equal(ErrorCategory.Workflow, exception.Category);
    }
}
