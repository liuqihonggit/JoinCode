namespace JoinCode.Abstractions.Interfaces;

public sealed record ApiRequest {
    /// <summary>获取 HTTP 方法。</summary>
    public required HttpMethod Method { get; init; }

    /// <summary>获取请求路径。</summary>
    public required string Path { get; init; }

    /// <summary>获取请求体内容。</summary>
    public string? Body { get; init; }

    /// <summary>获取请求头字典。</summary>
    public Dictionary<string, string>? Headers { get; init; }

    /// <summary>获取查询参数字典。</summary>
    public Dictionary<string, string>? QueryParams { get; init; }

    /// <summary>获取请求超时时间。</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>获取是否跳过重试。</summary>
    public bool SkipRetry { get; init; }

    /// <summary>创建 GET 请求。</summary>
    /// <param name="path">请求路径。</param>
    /// <param name="queryParams">查询参数。</param>
    public static ApiRequest Get(string path, Dictionary<string, string>? queryParams = null) => new() {
        Method = HttpMethod.Get,
        Path = path,
        QueryParams = queryParams
    };

    /// <summary>创建 POST 请求。</summary>
    /// <param name="path">请求路径。</param>
    /// <param name="body">请求体。</param>
    public static ApiRequest Post(string path, string? body = null) => new() {
        Method = HttpMethod.Post,
        Path = path,
        Body = body
    };

    /// <summary>创建 PUT 请求。</summary>
    /// <param name="path">请求路径。</param>
    /// <param name="body">请求体。</param>
    public static ApiRequest Put(string path, string? body = null) => new() {
        Method = HttpMethod.Put,
        Path = path,
        Body = body
    };

    /// <summary>创建 PATCH 请求。</summary>
    /// <param name="path">请求路径。</param>
    /// <param name="body">请求体。</param>
    public static ApiRequest Patch(string path, string? body = null) => new() {
        Method = HttpMethod.Patch,
        Path = path,
        Body = body
    };

    /// <summary>创建 DELETE 请求。</summary>
    /// <param name="path">请求路径。</param>
    public static ApiRequest Delete(string path) => new() {
        Method = HttpMethod.Delete,
        Path = path
    };
}

/// <summary>
/// API 响应结果
/// </summary>
public sealed record ApiResponse<T> {
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
    public static ApiResponse<T> SuccessResult(T data, int statusCode, Dictionary<string, List<string>>? headers = null, string? rawContent = null) => new() {
        Success = true,
        Data = data,
        StatusCode = statusCode,
        Headers = headers,
        RawContent = rawContent
    };

    /// <summary>
    /// 创建失败响应
    /// </summary>
    public static ApiResponse<T> Failure(int statusCode, string errorMessage, Dictionary<string, List<string>>? headers = null, string? rawContent = null) => new() {
        Success = false,
        StatusCode = statusCode,
        ErrorMessage = errorMessage,
        Headers = headers,
        RawContent = rawContent
    };
}

public interface IApiClient {
    /// <summary>发送 HTTP 请求并返回原始响应消息。</summary>
    /// <param name="request">API 请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<HttpResponseMessage> SendAsync(ApiRequest request, CancellationToken cancellationToken = default);

    /// <summary>发送请求并返回响应结果。</summary>
    /// <typeparam name="T">响应数据类型。</typeparam>
    /// <param name="request">API 请求。</param>
    /// <param name="jsonTypeInfo">JSON 类型信息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<ApiResponse<T>> RequestAsync<T>(ApiRequest request, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default);

    /// <summary>发送请求并在失败时抛出异常。</summary>
    /// <typeparam name="T">响应数据类型。</typeparam>
    /// <param name="request">API 请求。</param>
    /// <param name="jsonTypeInfo">JSON 类型信息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<T> RequestOrThrowAsync<T>(ApiRequest request, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default);

    /// <summary>发送 GET 请求。</summary>
    /// <typeparam name="T">响应数据类型。</typeparam>
    /// <param name="path">请求路径。</param>
    /// <param name="jsonTypeInfo">JSON 类型信息。</param>
    /// <param name="queryParams">查询参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<ApiResponse<T>> GetAsync<T>(string path, JsonTypeInfo<T> jsonTypeInfo, Dictionary<string, string>? queryParams = null, CancellationToken cancellationToken = default);

    /// <summary>发送 POST 请求。</summary>
    /// <typeparam name="T">响应数据类型。</typeparam>
    /// <param name="path">请求路径。</param>
    /// <param name="body">请求体。</param>
    /// <param name="jsonTypeInfo">JSON 类型信息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<ApiResponse<T>> PostAsync<T>(string path, string? body, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default);

    /// <summary>发送 PUT 请求。</summary>
    /// <typeparam name="T">响应数据类型。</typeparam>
    /// <param name="path">请求路径。</param>
    /// <param name="body">请求体。</param>
    /// <param name="jsonTypeInfo">JSON 类型信息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<ApiResponse<T>> PutAsync<T>(string path, string? body, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default);

    /// <summary>发送 PATCH 请求。</summary>
    /// <typeparam name="T">响应数据类型。</typeparam>
    /// <param name="path">请求路径。</param>
    /// <param name="body">请求体。</param>
    /// <param name="jsonTypeInfo">JSON 类型信息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<ApiResponse<T>> PatchAsync<T>(string path, string? body, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default);

    /// <summary>发送 DELETE 请求。</summary>
    /// <typeparam name="T">响应数据类型。</typeparam>
    /// <param name="path">请求路径。</param>
    /// <param name="jsonTypeInfo">JSON 类型信息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<ApiResponse<T>> DeleteAsync<T>(string path, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default);

    /// <summary>设置默认请求头。</summary>
    /// <param name="name">头名称。</param>
    /// <param name="value">头值。</param>
    void SetDefaultHeader(string name, string value);

    /// <summary>移除默认请求头。</summary>
    /// <param name="name">头名称。</param>
    void RemoveDefaultHeader(string name);

    /// <summary>设置授权令牌。</summary>
    /// <param name="token">令牌值。</param>
    /// <param name="scheme">认证方案。</param>
    void SetAuthorizationToken(string token, string scheme = "Bearer");

    /// <summary>清除授权信息。</summary>
    void ClearAuthorization();
}