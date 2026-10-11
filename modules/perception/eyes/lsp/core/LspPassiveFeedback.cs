namespace Services.Lsp.Internal;

/// <summary>
/// LSP 被动反馈接口 — 注册 LSP 服务器的通知和请求处理器
/// </summary>
public interface ILspPassiveFeedback {
    /// <summary>
    /// 注册通知处理器 — 为所有 LSP 服务器注册诊断发布和 workspace/configuration 处理器
    /// </summary>
    /// <param name="manager">LSP 管理器</param>
    void RegisterNotificationHandlers(ILspManager manager);
}

/// <summary>
/// LSP 被动反馈实现 — 处理 textDocument/publishDiagnostics 通知和 workspace/configuration 请求
/// </summary>
[Register(typeof(ILspPassiveFeedback), ServiceLifetime.Singleton)]
public sealed partial class LspPassiveFeedback : ServiceEntity, ILspPassiveFeedback {

    /// <summary>
    /// 构造 LSP 被动反馈处理器
    /// </summary>
    /// <param name="diagnosticRegistry">诊断注册表</param>
    /// <param name="logger">可选日志记录器</param>
    public LspPassiveFeedback(ILspDiagnosticRegistry diagnosticRegistry, ILogger<LspPassiveFeedback>? logger = null) {
        _diagnosticRegistry = diagnosticRegistry;
        _logger = logger;
    }
    private readonly ILspDiagnosticRegistry _diagnosticRegistry;
    private readonly ILogger<LspPassiveFeedback>? _logger;

    /// <summary>
    /// 注册通知处理器 — 为所有 LSP 服务器注册诊断发布和 workspace/configuration 处理器
    /// </summary>
    /// <param name="manager">LSP 管理器</param>
    public void RegisterNotificationHandlers(ILspManager manager) {
        var servers = manager.GetAllServers();
        var successCount = 0;

        foreach (var kvp in servers) {
            var serverName = kvp.Key;
            var serverInstance = kvp.Value;

            try {
                RegisterDiagnosticsHandler(serverName, serverInstance);
                RegisterWorkspaceConfigurationHandler(serverName, serverInstance);
                successCount++;
            } catch (Exception ex) {
                _logger?.LogError(ex, "Failed to register diagnostics handler for {ServerName}", serverName);
            }
        }

        _logger?.LogInformation("Registered LSP notification handlers for {SuccessCount}/{TotalCount} server(s)",
            successCount, servers.Count);
    }

    private void RegisterDiagnosticsHandler(string serverName, ILspServerInstance serverInstance) {
        serverInstance.OnNotification("textDocument/publishDiagnostics",
            async (node, ct) => {
                try {
                    var diagsParams = RelaxedJsonSerializer.Deserialize(
                        node?.ToJsonString() ?? string.Empty, LspJsonContext.Default.LspPublishDiagnosticsParams);
                    if (diagsParams is null || diagsParams.Diagnostics.Count == 0) {
                        return;
                    }

                    var diagnosticFiles = FormatDiagnosticsForAttachment(diagsParams.Uri, diagsParams.Diagnostics);
                    if (diagnosticFiles.Diagnostics.Count == 0) return;

                    _diagnosticRegistry.RegisterPending(serverName, [diagnosticFiles]);
                } catch (Exception ex) {
                    _logger?.LogDebug(ex, "Error processing diagnostics from {ServerName}", serverName);
                }

                await ValueTask.CompletedTask.ConfigureAwait(false);
            });
    }

    private void RegisterWorkspaceConfigurationHandler(string serverName, ILspServerInstance serverInstance) {
        serverInstance.OnRequest("workspace/configuration",
            (requestId, node, ct) => {
                _logger?.LogDebug("LSP: Received workspace/configuration request from {ServerName}", serverName);

                var result = new JsonArray();
                var configParams = RelaxedJsonSerializer.Deserialize(
                    node?.ToJsonString() ?? string.Empty, LspJsonContext.Default.LspConfigurationParams);
                if (configParams is not null) {
                    for (var i = 0; i < configParams.Items.Count; i++) {
                        result.Add(null);
                    }
                }

                return new ValueTask<JsonNode?>(result);
            });
    }

    private static LspDiagnosticFile FormatDiagnosticsForAttachment(string uri, List<LspDiagnosticDto> diagnostics) {
        var items = new List<LspDiagnosticItem>();

        foreach (var diag in diagnostics) {
            if (string.IsNullOrEmpty(diag.Message)) continue;

            var severity = MapLspSeverity(diag.Severity);
            var code = diag.Code.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? null
                : diag.Code.ToString();

            items.Add(new LspDiagnosticItem {
                Message = diag.Message,
                Severity = severity,
                Range = diag.Range,
                Source = diag.Source,
                Code = code
            });
        }

        var fileUri = uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            ? Uri.TryCreate(uri, UriKind.Absolute, out var u) ? u.LocalPath : uri
            : uri;

        return new LspDiagnosticFile {
            Uri = fileUri,
            Diagnostics = items
        };
    }

    private static string MapLspSeverity(int? lspSeverity) => lspSeverity switch {
        1 => "Error",
        2 => "Warning",
        3 => "Info",
        4 => "Hint",
        _ => "Error"
    };
}

/// <summary>
/// textDocument/publishDiagnostics 通知参数 DTO
/// </summary>
public sealed class LspPublishDiagnosticsParams {
    /// <summary>文档 URI</summary>
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    /// <summary>诊断列表</summary>
    [JsonPropertyName("diagnostics")]
    public List<LspDiagnosticDto> Diagnostics { get; set; } = [];
}

/// <summary>
/// LSP Diagnostic DTO — 单条诊断的 JSON 反序列化模型
/// </summary>
public sealed class LspDiagnosticDto {
    /// <summary>诊断消息</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>严重级别（1=Error, 2=Warning, 3=Info, 4=Hint）</summary>
    [JsonPropertyName("severity")]
    public int? Severity { get; set; }

    /// <summary>诊断范围</summary>
    [JsonPropertyName("range")]
    public LspRange? Range { get; set; }

    /// <summary>诊断来源</summary>
    [JsonPropertyName("source")]
    public string? Source { get; set; }

    /// <summary>诊断代码（LSP 规范允许 integer 或 string，用 JsonElement 兼容两种类型）</summary>
    [JsonPropertyName("code")]
    public JsonElement Code { get; set; }
}

/// <summary>
/// workspace/configuration 请求参数 DTO
/// </summary>
public sealed class LspConfigurationParams {
    /// <summary>配置项请求列表</summary>
    [JsonPropertyName("items")]
    public List<JsonElement> Items { get; set; } = [];
}