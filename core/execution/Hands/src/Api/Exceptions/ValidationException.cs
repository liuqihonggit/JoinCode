
namespace Services.Api;

/// <summary>
/// 验证错误异常 - 当 API 返回 400/422 状态码时抛出
/// </summary>
public sealed class ValidationException : ApiException
{
    private readonly Dictionary<string, List<string>> _errors;

    /// <summary>
    /// 字段验证错误
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Errors =>
        _errors.ToFrozenDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<string>)kvp.Value.AsReadOnly());

    /// <summary>
    /// 创建 ValidationException
    /// </summary>
    public ValidationException(
        string endpoint,
        string message,
        Dictionary<string, List<string>>? errors = null,
        string? responseContent = null)
        : base(
            $"API 请求验证失败: {message}",
            statusCode: 400,
            endpoint: endpoint,
            responseContent: responseContent,
            errorCode: global::JoinCode.Abstractions.Exceptions.ErrorCode.ApiValidation.ToValue())
    {
        _errors = errors ?? new Dictionary<string, List<string>>();
    }

    /// <inheritdoc />
    public override bool IsRetryable => false;
}
