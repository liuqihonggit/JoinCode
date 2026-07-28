
namespace McpClient;

public sealed partial class McpChannelNotificationHandler
{
    [Inject] private readonly ILogger<McpChannelNotificationHandler>? _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<string, TaskCompletionSource<ChannelPermissionResponse>> _pendingRequests = new();

    public event EventHandler<McpChannelMessageEventArgs>? ChannelMessageReceived;
    public event EventHandler<McpChannelPermissionResponseEventArgs>? PermissionResponseReceived;

    public McpChannelNotificationHandler(ILogger<McpChannelNotificationHandler>? logger = null)
    {
        _logger = logger;
    }

    public static bool SupportsChannel(ServerCapabilities? capabilities)
    {
        return false;
    }

    public static bool SupportsChannelPermission(ServerCapabilities? capabilities)
    {
        return false;
    }

    public static bool SupportsChannel(JsonElement? capabilitiesExperimental)
    {
        if (capabilitiesExperimental == null) return false;
        try
        {
            if (capabilitiesExperimental.Value.TryGetProperty("claude/channel", out _))
            {
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public static bool SupportsChannelPermission(JsonElement? capabilitiesExperimental)
    {
        if (capabilitiesExperimental == null) return false;
        try
        {
            if (capabilitiesExperimental.Value.TryGetProperty("claude/channel/permission", out _))
            {
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public void HandleChannelNotification(string serverName, JsonElement? Params)
    {
        if (Params == null) return;

        string? content = null;
        Dictionary<string, string>? meta = null;

        if (Params.Value.TryGetProperty("content", out var contentEl))
        {
            content = contentEl.GetString();
        }

        if (Params.Value.TryGetProperty("meta", out var metaEl) && metaEl.ValueKind == JsonValueKind.Object)
        {
            meta = new Dictionary<string, string>();
            foreach (var prop in metaEl.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    meta[prop.Name] = prop.Value.GetString() ?? string.Empty;
                }
            }
        }

        if (string.IsNullOrEmpty(content)) return;

        var xmlMessage = WrapChannelMessage(serverName, content, meta);

        _logger?.LogInformation("Channel 消息: server={Server}, content={Content}", serverName, content);

        ChannelMessageReceived?.Invoke(this, new McpChannelMessageEventArgs
        {
            ServerName = serverName,
            Content = content,
            Meta = meta,
            XmlMessage = xmlMessage
        });
    }

    public void HandleChannelPermissionNotification(string serverName, JsonElement? Params)
    {
        if (Params == null) return;

        string? requestId = null;
        string? behavior = null;

        if (Params.Value.TryGetProperty("request_id", out var reqEl))
        {
            requestId = reqEl.GetString();
        }

        if (Params.Value.TryGetProperty("behavior", out var behEl))
        {
            behavior = behEl.GetString();
        }

        if (string.IsNullOrEmpty(requestId) || string.IsNullOrEmpty(behavior)) return;

        _logger?.LogInformation("Channel 权限回复: server={Server}, requestId={RequestId}, behavior={Behavior}", serverName, requestId, behavior);

        _lock.Wait();
        try
        {
            if (_pendingRequests.TryGetValue(requestId, out var tcs))
            {
                tcs.TrySetResult(new ChannelPermissionResponse
                {
                    Behavior = behavior,
                    FromServer = serverName
                });
                _pendingRequests.Remove(requestId);
            }
        }
        finally
        {
            _lock.Release();
        }

        PermissionResponseReceived?.Invoke(this, new McpChannelPermissionResponseEventArgs
        {
            RequestId = requestId,
            Behavior = behavior,
            FromServer = serverName
        });
    }

    public async Task<ChannelPermissionResponse?> WaitForPermissionResponseAsync(string requestId, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<ChannelPermissionResponse>();

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _pendingRequests[requestId] = tcs;
        }
        finally
        {
            _lock.Release();
        }

        try
        {
            using var cts = TimeoutHelper.CreateLinkedTimeout(cancellationToken, timeout);
            return await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch
        {
            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _pendingRequests.Remove(requestId);
            }
            finally
            {
                _lock.Release();
            }
            return null;
        }
    }

    public static string WrapChannelMessage(string serverName, string content, Dictionary<string, string>? meta)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"<channel source=\"{EscapeXmlAttr(serverName)}\"");

        if (meta != null)
        {
            foreach (var kvp in meta)
            {
                if (IsValidMetaKey(kvp.Key))
                {
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

    private static bool IsValidMetaKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        if (!char.IsLetter(key[0]) && key[0] != '_') return false;
        foreach (var c in key)
        {
            if (!char.IsLetterOrDigit(c) && c != '_') return false;
        }
        return true;
    }

    private static string EscapeXmlAttr(string value)
    {
        return value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}

public sealed partial class McpChannelMessageEventArgs : EventArgs
{
    public required string ServerName { get; init; }
    public required string Content { get; init; }
    public Dictionary<string, string>? Meta { get; init; }
    public required string XmlMessage { get; init; }
}

public sealed partial class McpChannelPermissionResponseEventArgs : EventArgs
{
    public required string RequestId { get; init; }
    public required string Behavior { get; init; }
    public required string FromServer { get; init; }
}

public sealed partial class ChannelPermissionResponse
{
    public required string Behavior { get; init; }
    public required string FromServer { get; init; }
}