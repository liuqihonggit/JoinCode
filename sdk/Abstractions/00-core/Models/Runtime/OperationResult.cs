namespace JoinCode.Abstractions.Models;

public class OperationResult<T> {
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorType { get; set; }

    public static OperationResult<T> Ok(T data) {
        return new OperationResult<T> {
            Success = true,
            Data = data
        };
    }

    public static OperationResult<T> Fail(string errorMessage, string? errorType = null) {
        return new OperationResult<T> {
            Success = false,
            ErrorMessage = errorMessage,
            ErrorType = errorType ?? "GeneralError"
        };
    }
}

public class OperationResult {
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorType { get; set; }

    public static OperationResult Ok() {
        return new OperationResult {
            Success = true
        };
    }

    public static OperationResult Fail(string errorMessage, string? errorType = null) {
        return new OperationResult {
            Success = false,
            ErrorMessage = errorMessage,
            ErrorType = errorType ?? "GeneralError"
        };
    }
}
