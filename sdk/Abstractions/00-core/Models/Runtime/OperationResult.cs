namespace JoinCode.Abstractions.Models;

public sealed class OperationResult<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorType { get; init; }

    public static OperationResult<T> Ok(T data)
    {
        return new OperationResult<T>
        {
            Success = true,
            Data = data
        };
    }

    public static OperationResult<T> Fail(string errorMessage, string? errorType = null)
    {
        return new OperationResult<T>
        {
            Success = false,
            ErrorMessage = errorMessage,
            ErrorType = errorType ?? "GeneralError"
        };
    }
}

public sealed class OperationResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorType { get; init; }

    public static OperationResult Ok()
    {
        return new OperationResult
        {
            Success = true
        };
    }

    public static OperationResult Fail(string errorMessage, string? errorType = null)
    {
        return new OperationResult
        {
            Success = false,
            ErrorMessage = errorMessage,
            ErrorType = errorType ?? "GeneralError"
        };
    }
}
