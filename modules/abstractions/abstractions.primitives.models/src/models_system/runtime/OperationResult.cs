namespace JoinCode.Abstractions.Models;

public sealed record OperationResult<T> {
    /// <summary>获取是否成功。</summary>
    public bool Success { get; init; }
    /// <summary>获取结果数据。</summary>
    public T? Data { get; init; }
    /// <summary>获取错误消息。</summary>
    public string? ErrorMessage { get; init; }
    /// <summary>获取错误类型。</summary>
    public string? ErrorType { get; init; }

    /// <summary>
    /// 获取数据（仅当 Success 为 true 时调用）
    /// </summary>
    public T GetData() => Data ?? throw new InvalidOperationException($"Cannot get data from failed result: {ErrorMessage}");

    /// <summary>创建成功结果。</summary>
    public static OperationResult<T> Ok(T data) {
        return new OperationResult<T> {
            Success = true,
            Data = data
        };
    }

    /// <summary>创建失败结果。</summary>
    public static OperationResult<T> Fail(string errorMessage, string? errorType = null) {
        return new OperationResult<T> {
            Success = false,
            ErrorMessage = errorMessage,
            ErrorType = errorType ?? "GeneralError"
        };
    }
}

public sealed record OperationResult {
    /// <summary>获取是否成功。</summary>
    public bool Success { get; init; }
    /// <summary>获取错误消息。</summary>
    public string? ErrorMessage { get; init; }
    /// <summary>获取错误类型。</summary>
    public string? ErrorType { get; init; }

    /// <summary>创建成功结果。</summary>
    public static OperationResult Ok() {
        return new OperationResult {
            Success = true
        };
    }

    /// <summary>创建失败结果。</summary>
    public static OperationResult Fail(string errorMessage, string? errorType = null) {
        return new OperationResult {
            Success = false,
            ErrorMessage = errorMessage,
            ErrorType = errorType ?? "GeneralError"
        };
    }
}