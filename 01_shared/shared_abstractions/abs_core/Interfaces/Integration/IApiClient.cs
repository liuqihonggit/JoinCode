namespace JoinCode.Abstractions.Interfaces;

public sealed record ApiRequest
{
    public required HttpMethod Method { get; init; }

    public required string Path { get; init; }

    public string? Body { get; init; }

    public Dictionary<string, string>? Headers { get; init; }

    public Dictionary<string, string>? QueryParams { get; init; }

    public TimeSpan? Timeout { get; init; }

    public bool SkipRetry { get; init; }

    public static ApiRequest Get(string path, Dictionary<string, string>? queryParams = null) => new()
    {
        Method = HttpMethod.Get,
        Path = path,
        QueryParams = queryParams
    };

    public static ApiRequest Post(string path, string? body = null) => new()
    {
        Method = HttpMethod.Post,
        Path = path,
        Body = body
    };

    public static ApiRequest Put(string path, string? body = null) => new()
    {
        Method = HttpMethod.Put,
        Path = path,
        Body = body
    };

    public static ApiRequest Patch(string path, string? body = null) => new()
    {
        Method = HttpMethod.Patch,
        Path = path,
        Body = body
    };

    public static ApiRequest Delete(string path) => new()
    {
        Method = HttpMethod.Delete,
        Path = path
    };
}

/// <summary>
/// API 响应结果
/// </summary>
public sealed record ApiResponse<T>
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// 响应数据
    /// </summary>
    public T? Data { get; init; }

    /// <summary>
    /// HTTP 状态码
    /// </summary>
    public required int StatusCode { get; init; }

    /// <summary>
    /// 原始响应内容
    /// </summary>
    public string? RawContent { get; init; }

    /// <summary>
    /// 响应头
    /// </summary>
    public Dictionary<string, List<string>>? Headers { get; init; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 创建成功响应
    /// </summary>
    public static ApiResponse<T> SuccessResult(T data, int statusCode, Dictionary<string, List<string>>? headers = null, string? rawContent = null) => new()
    {
        Success = true,
        Data = data,
        StatusCode = statusCode,
        Headers = headers,
        RawContent = rawContent
    };

    /// <summary>
    /// 创建失败响应
    /// </summary>
    public static ApiResponse<T> Failure(int statusCode, string errorMessage, Dictionary<string, List<string>>? headers = null, string? rawContent = null) => new()
    {
        Success = false,
        StatusCode = statusCode,
        ErrorMessage = errorMessage,
        Headers = headers,
        RawContent = rawContent
    };
}

public interface IApiClient
{
    Task<HttpResponseMessage> SendAsync(ApiRequest request, CancellationToken cancellationToken = default);

    Task<ApiResponse<T>> RequestAsync<T>(ApiRequest request, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default);

    Task<T> RequestOrThrowAsync<T>(ApiRequest request, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default);

    Task<ApiResponse<T>> GetAsync<T>(string path, JsonTypeInfo<T> jsonTypeInfo, Dictionary<string, string>? queryParams = null, CancellationToken cancellationToken = default);

    Task<ApiResponse<T>> PostAsync<T>(string path, string? body, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default);

    Task<ApiResponse<T>> PutAsync<T>(string path, string? body, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default);

    Task<ApiResponse<T>> PatchAsync<T>(string path, string? body, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default);

    Task<ApiResponse<T>> DeleteAsync<T>(string path, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default);

    void SetDefaultHeader(string name, string value);

    void RemoveDefaultHeader(string name);

    void SetAuthorizationToken(string token, string scheme = "Bearer");

    void ClearAuthorization();
}
