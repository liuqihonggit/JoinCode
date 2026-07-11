namespace JoinCode.Abstractions.Interfaces;

public interface IExceptionService {
    OperationResult<T> HandleException<T>(Exception ex);
    OperationResult HandleException(Exception ex);
    T ExecuteWithExceptionHandling<T>(Func<T> action, string defaultErrorMessage = "发生错误");
    Task<T> ExecuteWithExceptionHandlingAsync<T>(Func<Task<T>> action, string defaultErrorMessage = "发生错误", CancellationToken cancellationToken = default);
    void ExecuteWithExceptionHandling(Action action, string defaultErrorMessage = "发生错误");
    Task ExecuteWithExceptionHandlingAsync(Func<Task> action, string defaultErrorMessage = "发生错误", CancellationToken cancellationToken = default);
}
