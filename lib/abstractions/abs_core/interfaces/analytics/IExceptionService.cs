namespace JoinCode.Abstractions.Interfaces;

/// <summary>异常处理服务接口。</summary>
public interface IExceptionService {
    /// <summary>处理异常并返回带类型的结果。</summary>
    OperationResult<T> HandleException<T>(Exception ex);
    /// <summary>处理异常并返回操作结果。</summary>
    OperationResult HandleException(Exception ex);
    /// <summary>在异常处理包装下执行带返回值的函数。</summary>
    T ExecuteWithExceptionHandling<T>(Func<T> action, string defaultErrorMessage = "发生错误");
    /// <summary>在异常处理包装下异步执行带返回值的函数。</summary>
    Task<T> ExecuteWithExceptionHandlingAsync<T>(Func<Task<T>> action, string defaultErrorMessage = "发生错误", CancellationToken cancellationToken = default);
    /// <summary>在异常处理包装下执行无返回值的操作。</summary>
    void ExecuteWithExceptionHandling(Action action, string defaultErrorMessage = "发生错误");
    /// <summary>在异常处理包装下异步执行无返回值的操作。</summary>
    Task ExecuteWithExceptionHandlingAsync(Func<Task> action, string defaultErrorMessage = "发生错误", CancellationToken cancellationToken = default);
}
