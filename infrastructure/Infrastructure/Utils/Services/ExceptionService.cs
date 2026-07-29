
namespace Core.Utils;

[Register]
public sealed partial class ExceptionService : IExceptionService {
    [Inject] private readonly ILogger<ExceptionService> _logger;
    [Inject] private readonly ITelemetryService? _telemetryService;

    public OperationResult<T> HandleException<T>(Exception ex) {
        _logger.LogError(ex, "发生异常: {ErrorCode} - {Message}", GetErrorCode(ex), ex.Message);
        RecordExceptionMetrics(ex);
        var (message, errorCode) = GetExceptionDetails(ex);
        return OperationResult<T>.Fail(message, errorCode);
    }

    public OperationResult HandleException(Exception ex) {
        _logger.LogError(ex, "发生异常: {ErrorCode} - {Message}", GetErrorCode(ex), ex.Message);
        RecordExceptionMetrics(ex);
        var (message, errorCode) = GetExceptionDetails(ex);
        return OperationResult.Fail(message, errorCode);
    }

    private static string GetErrorCode(Exception ex) => ex switch {
        WorkflowException workflowEx => workflowEx.ErrorCode,
        _ => global::JoinCode.Abstractions.Exceptions.ErrorCode.General.ToValue()
    };

    private static (string Message, string ErrorCode) GetExceptionDetails(Exception ex) {
        return ex switch {
            ConfigurationException configEx => ($"配置错误 [{configEx.ErrorCode}]: {configEx.Message}", configEx.ErrorCode),
            ApiException apiEx => ($"API 错误 [{apiEx.ErrorCode}]: {apiEx.Message}", apiEx.ErrorCode),
            CodeExecutionException codeEx => ($"代码执行错误 [{codeEx.ErrorCode}]: {codeEx.Message}", codeEx.ErrorCode),
            PermissionDeniedException permEx => ($"权限错误 [{permEx.ErrorCode}]: {permEx.Message}", permEx.ErrorCode),
            WorkflowException workflowEx => ($"工作流错误 [{workflowEx.ErrorCode}]: {workflowEx.Message}", workflowEx.ErrorCode),
            OperationCanceledException => ("操作已取消", global::JoinCode.Abstractions.Exceptions.ErrorCode.OperationCancelled.ToValue()),
            _ => ($"发生意外错误: {ex.Message}", global::JoinCode.Abstractions.Exceptions.ErrorCode.General.ToValue())
        };
    }

    public T ExecuteWithExceptionHandling<T>(Func<T> action, string defaultErrorMessage = "发生错误") {
        try {
            return action();
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            _logger.LogError(ex, "执行异常处理时出错");
            throw new WorkflowException(defaultErrorMessage, ex, global::JoinCode.Abstractions.Exceptions.ErrorCode.WorkflowExecution.ToValue());
        }
    }

    public async Task<T> ExecuteWithExceptionHandlingAsync<T>(Func<Task<T>> action, string defaultErrorMessage = "发生错误", CancellationToken cancellationToken = default) {
        try {
            return await action().ConfigureAwait(false);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            _logger.LogError(ex, "异步执行异常处理时出错");
            throw new WorkflowException(defaultErrorMessage, ex, global::JoinCode.Abstractions.Exceptions.ErrorCode.WorkflowExecution.ToValue());
        }
    }

    public void ExecuteWithExceptionHandling(Action action, string defaultErrorMessage = "发生错误") {
        try {
            action();
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            _logger.LogError(ex, "执行异常处理时出错");
            throw new WorkflowException(defaultErrorMessage, ex, global::JoinCode.Abstractions.Exceptions.ErrorCode.WorkflowExecution.ToValue());
        }
    }

    public async Task ExecuteWithExceptionHandlingAsync(Func<Task> action, string defaultErrorMessage = "发生错误", CancellationToken cancellationToken = default) {
        try {
            await action().ConfigureAwait(false);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            _logger.LogError(ex, "异步执行异常处理时出错");
            throw new WorkflowException(defaultErrorMessage, ex, global::JoinCode.Abstractions.Exceptions.ErrorCode.WorkflowExecution.ToValue());
        }
    }

    private void RecordExceptionMetrics(Exception ex) =>
        _telemetryService?.RecordCount("exception.handled.count", new Dictionary<string, string> { ["error_code"] = GetErrorCode(ex), ["type"] = ex.GetType().Name }, "count", "Exception handled count");
}
