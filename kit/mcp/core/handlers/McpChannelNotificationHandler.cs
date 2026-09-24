
namespace McpClient;

/// <summary>
/// MCP Channel 通知处理器 — 接收并分发 Channel 消息和权限响应通知
/// </summary>
public sealed partial class McpChannelNotificationHandler {
    private readonly ILogger<McpChannelNotificationHandler>? _logger;
    private ImmutableDictionary<string, TaskCompletionSource<ChannelPermissionResponse>> _pendingRequests = ImmutableDictionary<string, TaskCompletionSource<ChannelPermissionResponse>>.Empty;

    /// <summary>接收到 Channel 消息时触发</summary>
    public event EventHandler<McpChannelMessageEventArgs>? ChannelMessageReceived;
    /// <summary>接收到 Channel 权限响应时触发</summary>
    public event EventHandler<McpChannelPermissionResponseEventArgs>? PermissionResponseReceived;

    /// <summary>
    /// 初始化 MCP Channel 通知处理器
    /// </summary>
    /// <param name="logger">日志记录器（可选）</param>
    public McpChannelNotificationHandler(ILogger<McpChannelNotificationHandler>? logger = null) {
        _logger = logger;
    }

    /// <summary>
    /// 判断服务器能力是否支持 Channel（基于 ServerCapabilities）
    /// </summary>
    /// <param name="capabilities">服务器能力声明</param>
    /// <returns>当前实现始终返回 false（保留接口）</returns>
    public static bool SupportsChannel(ServerCapabilities? capabilities) {
        return false;
    }

    /// <summary>
    /// 判断服务器能力是否支持 Channel 权限（基于 ServerCapabilities）
    /// </summary>
    /// <param name="capabilities">服务器能力声明</param>
    /// <returns>当前实现始终返回 false（保留接口）</returns>
    public static bool SupportsChannelPermission(ServerCapabilities? capabilities) {
        return false;
    }

    /// <summary>
    /// 判断服务器能力是否支持 Channel（基于 experimental 扩展字段）
    /// </summary>
    /// <param name="capabilitiesExperimental">experimental 能力 JSON 元素</param>
    /// <returns>若存在 "claude/channel" 字段返回 true；否则 false</returns>
    public static bool SupportsChannel(JsonElement? capabilitiesExperimental) {
        if (capabilitiesExperimental == null) return false;
        try {
            if (capabilitiesExperimental.Value.TryGetProperty("claude/channel", out _)) {
                return true;
            }
            return false;
        } catch {
            return false;
        }
    }

    /// <summary>
    /// 判断服务器能力是否支持 Channel 权限（基于 experimental 扩展字段）
    /// </summary>
    /// <param name="capabilitiesExperimental">experimental 能力 JSON 元素</param>
    /// <returns>若存在 "claude/channel/permission" 字段返回 true；否则 false</returns>
    public static bool SupportsChannelPermission(JsonElement? capabilitiesExperimental) {
        if (capabilitiesExperimental == null) return false;
        try {
            if (capabilitiesExperimental.Value.TryGetProperty("claude/channel/permission", out _)) {
                return true;
            }
            return false;
        } catch {
            return false;
        }
    }

    /// <summary>
    /// 处理 Channel 消息通知，解析 content 和 meta 后触发 <see cref="ChannelMessageReceived"/> 事件
    /// </summary>
    /// <param name="serverName">来源服务器名称</param>
    /// <param name="Params">通知参数 JSON 元素</param>
    public void HandleChannelNotification(string serverName, JsonElement? Params) {
        if (Params == null) return;

        string? content = null;
        Dictionary<string, string>? meta = null;

        if (Params.Value.TryGetProperty("content", out var contentEl)) {
            content = contentEl.GetString();
        }

        if (Params.Value.TryGetProperty("meta", out var metaEl) && metaEl.ValueKind == JsonValueKind.Object) {
            meta = new Dictionary<string, string>();
            foreach (var prop in metaEl.EnumerateObject()) {
                if (prop.Value.ValueKind == JsonValueKind.String) {
                    meta[prop.Name] = prop.Value.GetString() ?? string.Empty;
                }
            }
        }

        if (string.IsNullOrEmpty(content)) return;

        var xmlMessage = WrapChannelMessage(serverName, content, meta);

        _logger?.LogInformation("Channel 消息: server={Server}, content={Content}", serverName, content);

        ChannelMessageReceived?.Invoke(this, new McpChannelMessageEventArgs {
            ServerName = serverName,
            Content = content,
            Meta = meta ?? [],
            XmlMessage = xmlMessage
        });
    }

    /// <summary>
    /// 处理 Channel 权限响应通知，完成对应的等待任务并触发 <see cref="PermissionResponseReceived"/> 事件
    /// </summary>
    /// <param name="serverName">来源服务器名称</param>
    /// <param name="Params">通知参数 JSON 元素（含 request_id 和 behavior）</param>
    public void HandleChannelPermissionNotification(string serverName, JsonElement? Params) {
        if (Params == null) return;

        string? requestId = null;
        string? behavior = null;

        if (Params.Value.TryGetProperty("request_id", out var reqEl)) {
            requestId = reqEl.GetString();
        }

        if (Params.Value.TryGetProperty("behavior", out var behEl)) {
            behavior = behEl.GetString();
        }

        if (string.IsNullOrEmpty(requestId) || string.IsNullOrEmpty(behavior)) return;

        _logger?.LogInformation("Channel 权限回复: server={Server}, requestId={RequestId}, behavior={Behavior}", serverName, requestId, behavior);

        if (_pendingRequests.TryGetValue(requestId, out var tcs)) {
            tcs.TrySetResult(new ChannelPermissionResponse {
                Behavior = behavior,
                FromServer = serverName
            });
            while (true) {
                var current = _pendingRequests;
                var updated = current.Remove(requestId);
                if (Interlocked.CompareExchange(ref _pendingRequests, updated, current) == current) break;
            }
        }

        PermissionResponseReceived?.Invoke(this, new McpChannelPermissionResponseEventArgs {
            RequestId = requestId,
            Behavior = behavior,
            FromServer = serverName
        });
    }

    /// <summary>
    /// 异步等待指定请求 ID 的权限响应，超时或取消时返回 null
    /// </summary>
    /// <param name="requestId">请求标识</param>
    /// <param name="timeout">等待超时时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>权限响应；超时或取消时返回 null</returns>
    public async Task<ChannelPermissionResponse?> WaitForPermissionResponseAsync(string requestId, TimeSpan timeout, CancellationToken cancellationToken = default) {
        var tcs = new TaskCompletionSource<ChannelPermissionResponse>();
        while (true) {
            var current = _pendingRequests;
            var updated = current.SetItem(requestId, tcs);
            if (Interlocked.CompareExchange(ref _pendingRequests, updated, current) == current) break;
        }

        try {
            using var cts = TimeoutHelper.CreateLinkedTimeout(cancellationToken, timeout);
            return await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
        } catch {
            while (true) {
                var current = _pendingRequests;
                var updated = current.Remove(requestId);
                if (Interlocked.CompareExchange(ref _pendingRequests, updated, current) == current) break;
            }
            return null;
        }
    }

    /// <summary>
    /// 将 Channel 消息包装为 XML 格式字符串，meta 字段作为 XML 属性输出
    /// </summary>
    /// <param name="serverName">来源服务器名称</param>
    /// <param name="content">消息内容</param>
    /// <param name="meta">元数据键值对（可选，仅合法 XML 属性名会被输出）</param>
    /// <returns>包装后的 XML 字符串</returns>
    public static string WrapChannelMessage(string serverName, string content, Dictionary<string, string>? meta) {
        var sb = new System.Text.StringBuilder();
        sb.Append($"<channel source=\"{EscapeXmlAttr(serverName)}\"");

        if (meta != null) {
            foreach (var kvp in meta) {
                if (IsValidMetaKey(kvp.Key)) {
                    sb.Append($" {kvp.Key}=\"{EscapeXmlAttr(kvp.Value)}\"");
                }
            }
        }

        sb.Append('>');
        sb.Append('\n');
        sb.Append(content);
        sb.Append('\n');
        sb.Append("</channel>");

        return sb.ToString();
    }

    private static bool IsValidMetaKey(string key) {
        if (string.IsNullOrEmpty(key)) return false;
        if (!char.IsLetter(key[0]) && key[0] != '_') return false;
        foreach (var c in key) {
            if (!char.IsLetterOrDigit(c) && c != '_') return false;
        }
        return true;
    }

    private static string EscapeXmlAttr(string value) {
        return value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}

/// <summary>
/// Channel 消息事件参数
/// </summary>
public sealed partial class McpChannelMessageEventArgs : EventArgs {
    /// <summary>来源服务器名称</summary>
    public required string ServerName { get; init; }
    /// <summary>消息内容</summary>
    public required string Content { get; init; }
    /// <summary>消息元数据</summary>
    public Dictionary<string, string> Meta { get; init; } = [];
    /// <summary>包装后的 XML 消息</summary>
    public required string XmlMessage { get; init; }
}

/// <summary>
/// Channel 权限响应事件参数
/// </summary>
public sealed partial class McpChannelPermissionResponseEventArgs : EventArgs {
    /// <summary>请求标识</summary>
    public required string RequestId { get; init; }
    /// <summary>权限行为（allow/deny 等）</summary>
    public required string Behavior { get; init; }
    /// <summary>来源服务器名称</summary>
    public required string FromServer { get; init; }
}

/// <summary>
/// Channel 权限响应数据
/// </summary>
public sealed partial class ChannelPermissionResponse {
    /// <summary>权限行为（allow/deny 等）</summary>
    public required string Behavior { get; init; }
    /// <summary>来源服务器名称</summary>
    public required string FromServer { get; init; }
}