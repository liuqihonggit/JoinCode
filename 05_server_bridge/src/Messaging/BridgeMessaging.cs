
namespace Core.Bridge;

/// <summary>
/// 服务器控制请求处理器回调集合 — 对齐 TS 端 ServerControlRequestHandlers
/// </summary>
public sealed class ServerControlRequestHandlers
{
    /// <summary>传输层实例（用于发送响应）</summary>
    public IReplBridgeTransport? Transport { get; init; }

    /// <summary>当前会话 ID</summary>
    public required string SessionId { get; init; }

    /// <summary>是否仅出站模式（所有可变请求返回错误）</summary>
    public bool OutboundOnly { get; init; }

    /// <summary>中断回调</summary>
    public Action? OnInterrupt { get; init; }

    /// <summary>设置模型回调</summary>
    public Action<string?>? OnSetModel { get; init; }

    /// <summary>设置最大思考令牌数回调</summary>
    public Action<int?>? OnSetMaxThinkingTokens { get; init; }

    /// <summary>
    /// 设置权限模式回调 — 返回裁决结果
    /// 对齐 TS 端: { ok: true } | { ok: false; error: string }
    /// </summary>
    public Func<string, OperationResult>? OnSetPermissionMode { get; init; }
}

// IngressMessageAction 已迁移到 JoinCode.Transport.Bridge 命名空间 (Transport.Contracts)

/// <summary>
/// Bridge 消息处理 — 对齐 TS 端 bridgeMessaging.ts
/// 纯函数式设计，无闭包状态，所有状态通过参数传入
/// </summary>
public static class BridgeMessaging
{
    /// <summary>
    /// 构造结果成功消息 — 对齐 TS 端 makeResultMessage
    /// 使用 StringBuilder 避免 JSON 注入，AOT 合规
    /// </summary>
    public static string MakeResultMessage(string sessionId)
    {
        // 使用 StringBuilder 构造 JSON，避免字符串插值导致的 JSON 注入
        return new System.Text.StringBuilder(256)
            .Append("{\"type\":\"result\",\"subtype\":\"success\",\"duration_ms\":0,\"duration_api_ms\":0,\"is_error\":false,\"num_turns\":0,\"result\":\"\",\"stop_reason\":null,\"total_cost_usd\":0,\"usage\":{},\"modelUsage\":{},\"permission_denials\":[],\"session_id\":\"")
            .Append(EscapeJsonString(sessionId))
            .Append("\",\"uuid\":\"")
            .Append(Guid.NewGuid().ToString("D"))
            .Append("\"}")
            .ToString();
    }

    /// <summary>
    /// 转义 JSON 字符串中的特殊字符 — 防止 JSON 注入
    /// </summary>
    private static string EscapeJsonString(ReadOnlySpan<char> value)
    {
        // 快速路径: 无需转义
        var needsEscape = false;
        foreach (var c in value)
        {
            if (c is '"' or '\\' or '\n' or '\r' or '\t')
            {
                needsEscape = true;
                break;
            }
        }

        if (!needsEscape) return value.ToString();

        var sb = new System.Text.StringBuilder(value.Length + 16);
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// 处理入站消息 — 对齐 TS 端 handleIngressMessage
    /// 三路路由: control_response / control_request / SDKMessage
    /// </summary>
    /// <param name="data">原始传输层消息字符串</param>
    /// <param name="recentPostedUUIDs">我们发出去的消息 UUID 集合（回声过滤）</param>
    /// <param name="recentInboundUUIDs">已转发过的入站消息 UUID 集合（防重复投递）</param>
    /// <param name="onInboundMessage">用户消息回调</param>
    /// <param name="onPermissionResponse">权限响应回调</param>
    /// <param name="onControlRequest">控制请求回调</param>
    /// <returns>处理结果动作</returns>
    public static IngressMessageAction HandleIngressMessage(
        string data,
        BoundedUUIDSet recentPostedUUIDs,
        BoundedUUIDSet recentInboundUUIDs,
        Action<string>? onInboundMessage = null,
        Action<JsonElement>? onPermissionResponse = null,
        Action<JsonElement>? onControlRequest = null)
    {
        try
        {
            // JSON 解析
            var parsed = JsonDocument.Parse(data);
            var root = parsed.RootElement;

            // 归一化键: camelCase requestId → snake_case request_id
            NormalizeControlMessageKeys(ref root);

            // 三路路由

            // 1. control_response — 远端客户端回答了权限提示
            if (IsControlResponse(root))
            {
                onPermissionResponse?.Invoke(root);
                return IngressMessageAction.PermissionResponse;
            }

            // 2. control_request — 服务器主动发来的控制请求
            if (IsControlRequest(root))
            {
                onControlRequest?.Invoke(root);
                return IngressMessageAction.ControlRequest;
            }

            // 3. SDKMessage — 普通消息
            if (!IsSDKMessage(root))
            {
                return IngressMessageAction.ParseError;
            }

            // UUID 回声过滤
            var uuid = GetUuid(root);
            if (uuid is not null && recentPostedUUIDs.Contains(uuid))
            {
                return IngressMessageAction.EchoFiltered;
            }

            // UUID 重复投递过滤
            if (uuid is not null && recentInboundUUIDs.Contains(uuid))
            {
                return IngressMessageAction.DuplicateFiltered;
            }

            // 仅转发 type === 'user' 的消息
            var messageType = GetMessageRole(root);
            if (messageType != "user")
            {
                return IngressMessageAction.NonUserIgnored;
            }

            // 转发用户消息
            if (uuid is not null)
            {
                recentInboundUUIDs.Add(uuid);
            }

            onInboundMessage?.Invoke(data);
            return IngressMessageAction.InboundMessage;
        }
        catch (Exception)
        {
            // 对齐 TS 端: 解析失败只记录调试日志，不抛异常
            return IngressMessageAction.ParseError;
        }
    }

    /// <summary>
    /// 处理服务器控制请求 — 对齐 TS 端 handleServerControlRequest
    /// 按 subtype 分派处理: initialize / set_model / set_max_thinking_tokens / set_permission_mode / interrupt
    /// </summary>
    public static async Task HandleServerControlRequestAsync(
        JsonElement request,
        ServerControlRequestHandlers handlers,
        CancellationToken ct = default)
    {
        if (handlers.Transport is null)
        {
            // 无法回复，直接返回
            return;
        }

        var requestId = GetRequestId(request);
        var subtype = GetSubtype(request);

        // outboundOnly 模式: 除 initialize 外所有可变请求返回错误
        if (handlers.OutboundOnly && subtype != "initialize")
        {
            await SendControlResponseAsync(
                handlers.Transport,
                requestId,
                handlers.SessionId,
                success: false,
                error: "This session is outbound-only and does not accept mutating control requests.",
                ct).ConfigureAwait(false);
            return;
        }

        switch (subtype)
        {
            case "initialize":
                await HandleInitializeAsync(handlers.Transport, requestId, handlers.SessionId, ct).ConfigureAwait(false);
                break;

            case "set_model":
                {
                    var model = GetRequestField(request, "model");
                    handlers.OnSetModel?.Invoke(model);
                    await SendControlResponseAsync(handlers.Transport, requestId, handlers.SessionId, success: true, ct: ct).ConfigureAwait(false);
                }
                break;

            case "set_max_thinking_tokens":
                {
                    var maxTokens = GetRequestIntField(request, "max_thinking_tokens");
                    handlers.OnSetMaxThinkingTokens?.Invoke(maxTokens);
                    await SendControlResponseAsync(handlers.Transport, requestId, handlers.SessionId, success: true, ct: ct).ConfigureAwait(false);
                }
                break;

            case "set_permission_mode":
                {
                    var mode = GetRequestField(request, "mode");
                    if (handlers.OnSetPermissionMode is not null && mode is not null)
                    {
                        var result = handlers.OnSetPermissionMode(mode);
                        if (result.Success)
                        {
                            await SendControlResponseAsync(handlers.Transport, requestId, handlers.SessionId, success: true, ct: ct).ConfigureAwait(false);
                        }
                        else
                        {
                            await SendControlResponseAsync(handlers.Transport, requestId, handlers.SessionId, success: false, error: result.ErrorMessage ?? "Permission mode change denied", ct: ct).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        await SendControlResponseAsync(handlers.Transport, requestId, handlers.SessionId, success: false, error: "Permission mode change not supported", ct: ct).ConfigureAwait(false);
                    }
                }
                break;

            case "interrupt":
                handlers.OnInterrupt?.Invoke();
                await SendControlResponseAsync(handlers.Transport, requestId, handlers.SessionId, success: true, ct: ct).ConfigureAwait(false);
                break;

            default:
                await SendControlResponseAsync(
                    handlers.Transport,
                    requestId,
                    handlers.SessionId,
                    success: false,
                    error: $"REPL bridge does not handle control_request subtype: {subtype}",
                    ct).ConfigureAwait(false);
                break;
        }
    }

    #region initialize 处理

    /// <summary>
    /// 处理 initialize 控制请求 — 对齐 TS 端
    /// 返回最小能力集（commands: [], models: [], account: {}, pid 等）
    /// </summary>
    private static async Task HandleInitializeAsync(
        IReplBridgeTransport transport,
        string requestId,
        string sessionId,
        CancellationToken ct)
    {
        var json = new StringBuilder()
            .Append("{\"type\":\"control_response\",\"request_id\":\"").Append(requestId)
            .Append("\",\"session_id\":\"").Append(sessionId)
            .Append("\",\"response\":{\"subtype\":\"initialize\",\"success\":true,\"commands\":[],\"models\":[],\"account\":{},\"pid\":")
            .Append(Environment.ProcessId)
            .Append("}}").ToString();
        await transport.WriteAsync(json, ct).ConfigureAwait(false);
    }

    #endregion

    #region 响应发送

    /// <summary>
    /// 发送控制响应 — 对齐 TS 端 transport.write(event)
    /// </summary>
    private static async Task SendControlResponseAsync(
        IReplBridgeTransport transport,
        string requestId,
        string sessionId,
        bool success,
        string? error = null,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder()
            .Append("{\"type\":\"control_response\",\"request_id\":\"").Append(requestId)
            .Append("\",\"session_id\":\"").Append(sessionId)
            .Append("\",\"response\":{\"success\":").Append(success ? "true" : "false");

        if (error is not null)
        {
            sb.Append(",\"error\":").Append(JsonEncode(error));
        }

        sb.Append("}}");
        await transport.WriteAsync(sb.ToString(), ct).ConfigureAwait(false);
    }

    /// <summary>JSON 字符串编码</summary>
    private static string JsonEncode(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    #endregion

    #region JSON 解析辅助

    /// <summary>判断是否为 control_response</summary>
    private static bool IsControlResponse(JsonElement root)
    {
        return root.TryGetProperty("type", out var typeProp)
            && typeProp.ValueEquals("control_response")
            && root.TryGetProperty("response", out _);
    }

    /// <summary>判断是否为 control_request</summary>
    private static bool IsControlRequest(JsonElement root)
    {
        return root.TryGetProperty("type", out var typeProp)
            && typeProp.ValueEquals("control_request")
            && root.TryGetProperty("request_id", out _)
            && root.TryGetProperty("request", out _);
    }

    /// <summary>判断是否为 SDKMessage（有 type 字段且为 string）</summary>
    private static bool IsSDKMessage(JsonElement root)
    {
        return root.TryGetProperty("type", out var typeProp)
            && typeProp.ValueKind == JsonValueKind.String;
    }

    /// <summary>获取消息 UUID</summary>
    private static string? GetUuid(JsonElement root)
    {
        if (root.TryGetProperty("uuid", out var uuidProp) && uuidProp.ValueKind == JsonValueKind.String)
        {
            return uuidProp.GetString();
        }
        return null;
    }

    /// <summary>
    /// 从 JSON 字符串中提取 UUID — 对齐 TS 端 m.uuid
    /// 用于 writeMessages 中的双层去重过滤
    /// </summary>
    public static string? ExtractUuid(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return GetUuid(doc.RootElement);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 从 JSON 消息中提取标题文本 — 对齐 TS 端 extractTitleText
    /// 仅提取用户消息（type=user）且非 meta、非 toolUseResult、非 compactSummary、origin 为 human 的文本
    /// </summary>
    public static string? ExtractTitleText(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // type !== 'user' → 跳过
            if (!root.TryGetProperty("type", out var typeProp) ||
                typeProp.ValueKind != JsonValueKind.String ||
                typeProp.GetString() != "user")
            {
                return null;
            }

            // isMeta → 跳过
            if (root.TryGetProperty("isMeta", out var metaProp) &&
                metaProp.ValueKind == JsonValueKind.True)
            {
                return null;
            }

            // toolUseResult → 跳过
            if (root.TryGetProperty("toolUseResult", out var toolResultProp) &&
                toolResultProp.ValueKind != JsonValueKind.Null &&
                toolResultProp.ValueKind != JsonValueKind.Undefined)
            {
                return null;
            }

            // isCompactSummary → 跳过
            if (root.TryGetProperty("isCompactSummary", out var compactProp) &&
                compactProp.ValueKind == JsonValueKind.True)
            {
                return null;
            }

            // origin.kind !== 'human' → 跳过
            if (root.TryGetProperty("origin", out var originProp) &&
                originProp.ValueKind == JsonValueKind.Object &&
                originProp.TryGetProperty("kind", out var kindProp) &&
                kindProp.ValueKind == JsonValueKind.String &&
                kindProp.GetString() != "human")
            {
                return null;
            }

            // 提取 message.content 文本
            if (!root.TryGetProperty("message", out var msgProp) ||
                msgProp.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!msgProp.TryGetProperty("content", out var contentProp))
            {
                return null;
            }

            // content 是字符串
            if (contentProp.ValueKind == JsonValueKind.String)
            {
                var text = contentProp.GetString();
                return string.IsNullOrEmpty(text) ? null : text.Trim();
            }

            // content 是数组，找第一个 type=text 的 block
            if (contentProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in contentProp.EnumerateArray())
                {
                    if (block.TryGetProperty("type", out var blockType) &&
                        blockType.ValueKind == JsonValueKind.String &&
                        blockType.GetString() == "text" &&
                        block.TryGetProperty("text", out var textProp) &&
                        textProp.ValueKind == JsonValueKind.String)
                    {
                        var text = textProp.GetString();
                        return string.IsNullOrEmpty(text) ? null : text.Trim();
                    }
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 向 JSON 消息注入 session_id 字段 — 对齐 TS 端 sdkMsg => ({ ...sdkMsg, session_id })
    /// 如果消息已包含 session_id 则不覆盖
    /// </summary>
    public static string InjectSessionId(string json, string sessionId)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            // 如果已有 session_id，不覆盖
            if (doc.RootElement.TryGetProperty("session_id", out _))
            {
                return json;
            }

            // 在 JSON 对象末尾注入 session_id
            var lastBrace = json.LastIndexOf('}');
            if (lastBrace <= 0) return json;

            var prefix = json[..lastBrace].TrimEnd();
            var separator = prefix.EndsWith('{') ? "" : ",";
            return $"{prefix}{separator}\"session_id\":\"{sessionId}\"}}";
        }
        catch
        {
            return json;
        }
    }

    /// <summary>获取消息角色（type 字段值）</summary>
    private static string? GetMessageRole(JsonElement root)
    {
        if (root.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String)
        {
            return typeProp.GetString();
        }
        return null;
    }

    /// <summary>获取 request_id</summary>
    private static string GetRequestId(JsonElement root)
    {
        if (root.TryGetProperty("request_id", out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString() ?? string.Empty;
        }
        // 兼容 camelCase
        if (root.TryGetProperty("requestId", out var camelProp) && camelProp.ValueKind == JsonValueKind.String)
        {
            return camelProp.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    /// <summary>获取 request.subtype</summary>
    private static string GetSubtype(JsonElement request)
    {
        if (request.TryGetProperty("request", out var reqProp)
            && reqProp.TryGetProperty("subtype", out var subtypeProp)
            && subtypeProp.ValueKind == JsonValueKind.String)
        {
            return subtypeProp.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    /// <summary>获取 request 中的字符串字段</summary>
    private static string? GetRequestField(JsonElement request, string fieldName)
    {
        if (request.TryGetProperty("request", out var reqProp)
            && reqProp.TryGetProperty(fieldName, out var fieldProp)
            && fieldProp.ValueKind == JsonValueKind.String)
        {
            return fieldProp.GetString();
        }
        return null;
    }

    /// <summary>获取 request 中的整数字段</summary>
    private static int? GetRequestIntField(JsonElement request, string fieldName)
    {
        if (request.TryGetProperty("request", out var reqProp)
            && reqProp.TryGetProperty(fieldName, out var fieldProp)
            && fieldProp.ValueKind == JsonValueKind.Number)
        {
            return fieldProp.GetInt32();
        }
        return null;
    }

    /// <summary>
    /// 归一化控制消息键 — 对齐 TS 端 normalizeControlMessageKeys
    /// 将 camelCase requestId 转为 snake_case request_id
    /// </summary>
    private static void NormalizeControlMessageKeys(ref JsonElement root)
    {
        // JsonElement 是只读的，这里仅做检查不做修改
        // 实际归一化在 GetRequestId 中通过双路径查找实现
    }

    #endregion

    #region 入站消息处理 — 对齐 TS 端 inboundMessages.ts

    /// <summary>
    /// 提取入站消息字段 — 对齐 TS 端 extractInboundMessageFields
    /// 从 SDKMessage 中提取 content 和 uuid
    /// </summary>
    public static InboundMessageFields? ExtractInboundMessageFields(JsonElement message)
    {
        if (!message.TryGetProperty("type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var type = typeProp.GetString() ?? string.Empty;

        // 提取 uuid
        string? uuid = null;
        if (message.TryGetProperty("uuid", out var uuidProp) && uuidProp.ValueKind == JsonValueKind.String)
        {
            uuid = uuidProp.GetString();
        }

        // 提取 content
        if (message.TryGetProperty("content", out var contentProp))
        {
            if (contentProp.ValueKind == JsonValueKind.String)
            {
                return new InboundMessageFields { Content = contentProp.GetString() ?? string.Empty, Uuid = uuid };
            }

            if (contentProp.ValueKind == JsonValueKind.Array)
            {
                // 归一化图片块
                var normalized = NormalizeImageBlocks(contentProp);
                return new InboundMessageFields { ContentBlocks = normalized, Uuid = uuid };
            }
        }

        return new InboundMessageFields { Content = string.Empty, Uuid = uuid };
    }

    /// <summary>
    /// 归一化图片块 — 对齐 TS 端 normalizeImageBlocks
    /// 将 camelCase mediaType 转为 snake_case media_type
    /// </summary>
    public static List<Dictionary<string, JsonElement>> NormalizeImageBlocks(JsonElement blocks)
    {
        var result = new List<Dictionary<string, JsonElement>>();

        foreach (var block in blocks.EnumerateArray())
        {
            var dict = new Dictionary<string, JsonElement>();
            foreach (var prop in block.EnumerateObject())
            {
                var key = prop.Name;
                // camelCase → snake_case 归一化
                if (key == "mediaType") key = "media_type";
                if (key == "data") key = "data"; // 保持不变

                dict[key] = prop.Value;
            }

            result.Add(dict);
        }

        return result;
    }

    /// <summary>
    /// 检测 base64 图片格式 — 对齐 TS 端 detectImageFormatFromBase64
    /// 通过 base64 前缀字节判断 PNG/JPEG/GIF/WebP
    /// </summary>
    public static string? DetectImageFormatFromBase64(ReadOnlySpan<char> base64)
    {
        try
        {
            // base64 解码前几个字节检查魔数
            var b64 = base64.Length > 32 ? base64.Slice(0, 32) : base64;
            var padding = b64.Length % 4;
            var str = b64.ToString();
            if (padding > 0) str += new string('=', 4 - padding);

            var bytes = Convert.FromBase64String(str);

            if (bytes.Length >= 8)
            {
                // PNG: 89 50 4E 47
                if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                    return "image/png";
                // JPEG: FF D8 FF
                if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
                    return "image/jpeg";
                // GIF: 47 49 46 38
                if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38)
                    return "image/gif";
                // WebP: 52 49 46 46 ... 57 45 42 50
                if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
                    && bytes.Length >= 12 && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
                    return "image/webp";
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    #endregion
}

// InboundMessageFields 已迁移到 JoinCode.Transport.Bridge 命名空间 (Transport.Contracts)
