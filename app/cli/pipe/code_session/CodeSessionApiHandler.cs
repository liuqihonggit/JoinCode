namespace JoinCode.Pipe;

/// <summary>代码会话 API 响应体 — 统一封装创建/查询/删除/列表等接口的返回结果</summary>
public sealed class CodeSessionApiResponse {
    /// <summary>操作是否成功</summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>会话标识符</summary>
    [JsonPropertyName("sessionId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SessionId { get; init; }

    /// <summary>项目名称</summary>
    [JsonPropertyName("projectName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProjectName { get; init; }

    /// <summary>工作目录路径</summary>
    [JsonPropertyName("workDirectory")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkDirectory { get; init; }

    /// <summary>会话状态</summary>
    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; init; }

    /// <summary>错误信息</summary>
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }
}

/// <summary>代码会话 API 处理器 — 提供创建/查询/删除/列表等 HTTP 接口，可选 JWT 鉴权</summary>
[Register(typeof(CodeSessionApiHandler), ServiceLifetime.Singleton)]
public sealed partial class CodeSessionApiHandler : ServiceEntity {
    private readonly CodeSessionManager _manager;
    private readonly Core.Bridge.BridgeJwtService? _jwtService;

    /// <summary>
    /// 构造函数 — 注入会话管理器与可选的 JWT 服务
    /// </summary>
    /// <param name="manager">代码会话管理器</param>
    /// <param name="jwtService">JWT 鉴权服务，为 null 时跳过鉴权</param>
    public CodeSessionApiHandler(CodeSessionManager manager, Core.Bridge.BridgeJwtService? jwtService = null) {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _jwtService = jwtService;
    }

    /// <summary>
    /// 创建新的代码会话
    /// </summary>
    /// <param name="projectName">项目名称</param>
    /// <param name="workDirectory">工作目录路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>包含新会话信息的 API 响应</returns>
    public async ValueTask<CodeSessionApiResponse> HandleCreateAsync(
        string projectName,
        string workDirectory,
        CancellationToken ct = default) {
        try {
            var session = await _manager.CreateSessionAsync(projectName, workDirectory, ct).ConfigureAwait(false);
            return new CodeSessionApiResponse {
                Success = true,
                SessionId = session.SessionId,
                ProjectName = session.ProjectName,
                WorkDirectory = session.WorkDirectory,
                Status = session.Status.ToString()
            };
        } catch (Exception ex) {
            return new CodeSessionApiResponse { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// 根据会话标识符查询会话详情
    /// </summary>
    /// <param name="sessionId">会话标识符</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>包含会话信息的 API 响应；不存在时 Success 为 false</returns>
    public async ValueTask<CodeSessionApiResponse> HandleGetAsync(
        string sessionId,
        CancellationToken ct = default) {
        try {
            var session = await _manager.GetSessionAsync(sessionId, ct).ConfigureAwait(false);
            if (session is null) {
                return new CodeSessionApiResponse { Success = false, Error = "Session not found" };
            }

            return new CodeSessionApiResponse {
                Success = true,
                SessionId = session.SessionId,
                ProjectName = session.ProjectName,
                WorkDirectory = session.WorkDirectory,
                Status = session.Status.ToString()
            };
        } catch (Exception ex) {
            return new CodeSessionApiResponse { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// 根据会话标识符删除会话
    /// </summary>
    /// <param name="sessionId">会话标识符</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>删除成功时 Success 为 true；会话不存在时为 false</returns>
    public async ValueTask<CodeSessionApiResponse> HandleDeleteAsync(
        string sessionId,
        CancellationToken ct = default) {
        try {
            var deleted = await _manager.DeleteSessionAsync(sessionId, ct).ConfigureAwait(false);
            return new CodeSessionApiResponse {
                Success = deleted,
                Error = deleted ? null : "Session not found"
            };
        } catch (Exception ex) {
            return new CodeSessionApiResponse { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// 列出全部代码会话
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>会话响应只读列表</returns>
    public async ValueTask<IReadOnlyList<CodeSessionApiResponse>> HandleListAsync(
        CancellationToken ct = default) {
        var sessions = await _manager.ListSessionsAsync(ct).ConfigureAwait(false);
        return sessions.Select(s => new CodeSessionApiResponse {
            Success = true,
            SessionId = s.SessionId,
            ProjectName = s.ProjectName,
            WorkDirectory = s.WorkDirectory,
            Status = s.Status.ToString()
        }).ToList();
    }

    /// <summary>
    /// 处理 HTTP 请求 — 根据路径与方法分发到对应的会话操作路由
    /// </summary>
    /// <param name="context">HTTP 监听上下文</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task HandleHttpRequestAsync(HttpListenerContext context, CancellationToken ct) {
        var path = context.Request.Url?.AbsolutePath ?? "/";
        var method = context.Request.HttpMethod;

        if (!ValidateToken(context)) {
            context.Response.StatusCode = 401;
            await WriteJsonAsync(context.Response, new CodeSessionApiResponse { Success = false, Error = "Unauthorized" }).ConfigureAwait(false);
            return;
        }

        switch (path) {
            case "/code-sessions" when method == "GET":
            await HandleListRouteAsync(context, ct).ConfigureAwait(false);
            break;

            case "/code-sessions" when method == "POST":
            await HandleCreateRouteAsync(context, ct).ConfigureAwait(false);
            break;

            case var p when p.StartsWith("/code-sessions/") && method == "GET":
            await HandleGetRouteAsync(context, p, ct).ConfigureAwait(false);
            break;

            case var p when p.StartsWith("/code-sessions/") && method == "DELETE":
            await HandleDeleteRouteAsync(context, p, ct).ConfigureAwait(false);
            break;

            default:
            context.Response.StatusCode = 404;
            await WriteJsonAsync(context.Response, new CodeSessionApiResponse { Success = false, Error = "Not found" }).ConfigureAwait(false);
            break;
        }
    }

    private bool ValidateToken(HttpListenerContext context) {
        if (_jwtService is null) {
            return true;
        }

        var authHeader = context.Request.Headers["Authorization"];
        if (string.IsNullOrEmpty(authHeader)) {
            return false;
        }

        var token = authHeader.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(token)) {
            return false;
        }

        var result = _jwtService.ValidateToken(token);
        return result.IsValid;
    }

    private async Task HandleListRouteAsync(HttpListenerContext context, CancellationToken ct) {
        var result = await HandleListAsync(ct).ConfigureAwait(false);
        await WriteJsonAsync(context.Response, result.ToList()).ConfigureAwait(false);
    }

    private async Task HandleCreateRouteAsync(HttpListenerContext context, CancellationToken ct) {
        using var reader = context.Request.InputStream.AsUtf8Reader();
        var body = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

        string projectName = string.Empty;
        string workDirectory = string.Empty;

        if (!string.IsNullOrEmpty(body)) {
            var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("projectName", out var pn)) {
                projectName = pn.GetString() ?? string.Empty;
            }
            if (doc.RootElement.TryGetProperty("workDirectory", out var wd)) {
                workDirectory = wd.GetString() ?? string.Empty;
            }
        }

        var result = await HandleCreateAsync(projectName, workDirectory, ct).ConfigureAwait(false);
        context.Response.StatusCode = result.Success ? 201 : 400;
        await WriteJsonAsync(context.Response, result).ConfigureAwait(false);
    }

    private async Task HandleGetRouteAsync(HttpListenerContext context, string path, CancellationToken ct) {
        var sessionId = path["/code-sessions/".Length..];
        var result = await HandleGetAsync(sessionId, ct).ConfigureAwait(false);
        context.Response.StatusCode = result.Success ? 200 : 404;
        await WriteJsonAsync(context.Response, result).ConfigureAwait(false);
    }

    private async Task HandleDeleteRouteAsync(HttpListenerContext context, string path, CancellationToken ct) {
        var sessionId = path["/code-sessions/".Length..];
        var result = await HandleDeleteAsync(sessionId, ct).ConfigureAwait(false);
        context.Response.StatusCode = result.Success ? 200 : 404;
        await WriteJsonAsync(context.Response, result).ConfigureAwait(false);
    }

    private static async Task WriteJsonAsync<T>(HttpListenerResponse response, T data, JsonTypeInfo<T> jsonTypeInfo) {
        var json = JsonSerializer.Serialize(data, jsonTypeInfo);
        var bytes = Encoding.UTF8.GetBytes(json);

        response.ContentType = "application/json";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        response.Close();
    }

    private static Task WriteJsonAsync(HttpListenerResponse response, CodeSessionApiResponse data) {
        return WriteJsonAsync(response, data, PipeJsonContext.Default.CodeSessionApiResponse);
    }

    private static Task WriteJsonAsync(HttpListenerResponse response, List<CodeSessionApiResponse> data) {
        return WriteJsonAsync(response, data, PipeJsonContext.Default.ListCodeSessionApiResponse);
    }
}