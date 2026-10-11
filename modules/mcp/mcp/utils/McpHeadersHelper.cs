namespace McpClient;

/// <summary>
/// MCP 请求头辅助方法 — 提供动态请求头获取和请求头合并功能
/// </summary>
public static class McpHeadersHelper {
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 执行 headersHelper 命令获取动态请求头 — 通过子进程执行命令，解析 JSON 输出为请求头字典
    /// </summary>
    /// <param name="serverName">MCP 服务器名称</param>
    /// <param name="serverUrl">MCP 服务器 URL</param>
    /// <param name="headersHelper">获取请求头的命令</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <param name="processService">进程服务（可选，默认使用工厂创建）</param>
    /// <returns>动态请求头字典；执行失败或返回空时为 null</returns>
    public static async Task<Dictionary<string, string>?> GetDynamicHeadersAsync(
        string serverName,
        string serverUrl,
        string headersHelper,
        ILogger? logger = null,
        CancellationToken cancellationToken = default,
        IProcessService? processService = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(headersHelper);

        try {
            logger?.LogDebug("执行 headersHelper 获取动态请求头: {ServerName}", serverName);

            var effectiveProcessService = processService ?? IO.ProcessService.ProcessServiceFactory.Create();
            var options = new ProcessOptions {
                FileName = "cmd.exe",
                ArgumentList = ["/c", headersHelper],
                TimeoutMs = (int)Timeout.TotalMilliseconds,
                EnvironmentVariables = new Dictionary<string, string> {
                    ["JCC_MCP_SERVER_NAME"] = serverName,
                    ["JCC_MCP_SERVER_URL"] = serverUrl
                }
            };

            var result = await effectiveProcessService.ExecuteAsync(options, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(result.StandardError)) {
                logger?.LogDebug("headersHelper stderr: {Stderr}", result.StandardError);
            }

            if (!result.Success || string.IsNullOrWhiteSpace(result.StandardOutput)) {
                logger?.LogWarning("headersHelper 执行失败: ExitCode={ExitCode}", result.ExitCode);
                return null;
            }

            var outputText = result.StandardOutput.Trim();
            var headers = RelaxedJsonSerializer.Deserialize(outputText, McpClientJsonContext.Default.DictionaryStringJsonElement);

            if (headers == null || headers.Count == 0) {
                logger?.LogWarning("headersHelper 返回空结果");
                return null;
            }

            var validatedHeaders = new Dictionary<string, string>(headers.Count);
            foreach (var kvp in headers) {
                if (kvp.Value.ValueKind == JsonValueKind.String) {
                    validatedHeaders[kvp.Key] = kvp.Value.GetString() ?? string.Empty;
                } else {
                    logger?.LogWarning("headersHelper 返回非字符串值: Key={Key}, Type={Type}", kvp.Key, kvp.Value.ValueKind);
                }
            }

            logger?.LogDebug("成功获取 {Count} 个动态请求头", validatedHeaders.Count);
            return validatedHeaders;
        } catch (OperationCanceledException) {
            logger?.LogWarning("headersHelper 执行超时: {ServerName}", serverName);
            return null;
        } catch (Exception ex) {
            logger?.LogError(ex, "headersHelper 执行失败: {ServerName}", serverName);
            return null;
        }
    }

    /// <summary>
    /// 合并静态请求头和动态请求头 — 动态请求头优先级高于静态请求头
    /// </summary>
    /// <param name="staticHeaders">静态请求头（可选）</param>
    /// <param name="dynamicHeaders">动态请求头（可选）</param>
    /// <returns>合并后的请求头字典</returns>
    public static Dictionary<string, string> CombineHeaders(
        Dictionary<string, string>? staticHeaders,
        Dictionary<string, string>? dynamicHeaders) {
        var result = new Dictionary<string, string>();

        if (staticHeaders is not null) {
            foreach (var kvp in staticHeaders) {
                result[kvp.Key] = kvp.Value;
            }
        }

        if (dynamicHeaders is not null) {
            foreach (var kvp in dynamicHeaders) {
                result[kvp.Key] = kvp.Value;
            }
        }

        return result;
    }
}