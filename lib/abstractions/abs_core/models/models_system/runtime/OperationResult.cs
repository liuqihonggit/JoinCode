namespace JoinCode.Abstractions.Models;

public sealed record OperationResult<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorType { get; init; }

    /// <summary>
    /// 获取数据（仅当 Success 为 true 时调用）
    /// </summary>
    public T GetData() => Data ?? throw new InvalidOperationException($"Cannot get data from failed result: {ErrorMessage}");

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

public sealed record OperationResult
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
