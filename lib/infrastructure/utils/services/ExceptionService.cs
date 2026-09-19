
namespace Core.Utils;

/// <summary>
/// 异常服务 — 统一处理异常日志、遥测指标和错误码转换
/// </summary>
[Register(typeof(IExceptionService), ServiceLifetime.Singleton)]
public sealed partial class ExceptionService : ServiceEntity, IExceptionService {

    /// <summary>
    /// 构造异常服务
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="telemetryService">遥测服务（可选）</param>
    public ExceptionService(ILogger<ExceptionService> logger, ITelemetryService? telemetryService = null) {
        _logger = logger;
        _telemetryService = telemetryService;
    }
    private readonly ILogger<ExceptionService> _logger;
    private readonly ITelemetryService? _telemetryService;

    /// <summary>
    /// 处理异常并返回带类型的失败结果
    /// </summary>
    /// <typeparam name="T">结果值类型</typeparam>
    /// <param name="ex">异常</param>
    /// <returns>失败的 OperationResult</returns>
    public OperationResult<T> HandleException<T>(Exception ex) {
        _logger.LogError(ex, "发生异常: {ErrorCode} - {Message}", GetErrorCode(ex), ex.Message);
        RecordExceptionMetrics(ex);
        var (message, errorCode) = GetExceptionDetails(ex);
        return OperationResult<T>.Fail(message, errorCode);
    }

    /// <summary>
    /// 处理异常并返回无类型的失败结果
    /// </summary>
    /// <param name="ex">异常</param>
    /// <returns>失败的 OperationResult</returns>
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
            WorkflowException workflowEx => ($"工作流错误 [{workflowEx.ErrorCode}]: {workflowEx.Message}", workflowEx.ErrorCode),
            OperationCanceledException => ("操作已取消", global::JoinCode.Abstractions.Exceptions.ErrorCode.OperationCancelled.ToValue()),
            _ => ($"发生意外错误: {ex.Message}", global::JoinCode.Abstractions.Exceptions.ErrorCode.General.ToValue())
        };
    }

    /// <summary>
    /// 执行带异常处理的操作，异常时包装为 WorkflowException 抛出
    /// </summary>
    /// <typeparam name="T">返回值类型</typeparam>
    /// <param name="action">要执行的操作</param>
    /// <param name="defaultErrorMessage">默认错误消息</param>
    /// <returns>操作返回值</returns>
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

    /// <summary>
    /// 异步执行带异常处理的操作，异常时包装为 WorkflowException 抛出
    /// </summary>
    /// <typeparam name="T">返回值类型</typeparam>
    /// <param name="action">要执行的异步操作</param>
    /// <param name="defaultErrorMessage">默认错误消息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作返回值</returns>
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

    /// <summary>
    /// 执行带异常处理的无返回值操作，异常时包装为 WorkflowException 抛出
    /// </summary>
    /// <param name="action">要执行的操作</param>
    /// <param name="defaultErrorMessage">默认错误消息</param>
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

    /// <summary>
    /// 异步执行带异常处理的无返回值操作，异常时包装为 WorkflowException 抛出
    /// </summary>
    /// <param name="action">要执行的异步操作</param>
    /// <param name="defaultErrorMessage">默认错误消息</param>
    /// <param name="cancellationToken">取消令牌</param>
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