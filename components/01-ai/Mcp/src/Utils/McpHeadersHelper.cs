namespace McpClient;

public static class McpHeadersHelper
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static async Task<Dictionary<string, string>?> GetDynamicHeadersAsync(
        string serverName,
        string serverUrl,
        string headersHelper,
        ILogger? logger = null,
        CancellationToken cancellationToken = default,
        IProcessService? processService = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headersHelper);

        try
        {
            logger?.LogDebug("执行 headersHelper 获取动态请求头: {ServerName}", serverName);

            ProcessResult result;
            if (processService is not null)
            {
                var options = new ProcessOptions
                {
                    FileName = "cmd.exe",
                    Arguments = "/c " + headersHelper,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8,
                    TimeoutMs = (int)Timeout.TotalMilliseconds,
                    EnvironmentVariables = new Dictionary<string, string>
                    {
                        ["CLAUDE_CODE_MCP_SERVER_NAME"] = serverName,
                        ["CLAUDE_CODE_MCP_SERVER_URL"] = serverUrl
                    }
                };

                result = await processService.ExecuteAsync(options, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c " + headersHelper,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };

                startInfo.EnvironmentVariables["CLAUDE_CODE_MCP_SERVER_NAME"] = serverName;
                startInfo.EnvironmentVariables["CLAUDE_CODE_MCP_SERVER_URL"] = serverUrl;

                using var process = new Process { StartInfo = startInfo };
                process.Start();

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(Timeout);

                var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
                var errorTask = process.StandardError.ReadToEndAsync(cts.Token);
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);

                var output = await outputTask.ConfigureAwait(false);
                var stderr = await errorTask.ConfigureAwait(false);

                result = new ProcessResult
                {
                    ExitCode = process.ExitCode,
                    StandardOutput = output,
                    StandardError = stderr,
                    ExecutionTime = TimeSpan.Zero
                };
            }

            if (!string.IsNullOrWhiteSpace(result.StandardError))
            {
                logger?.LogDebug("headersHelper stderr: {Stderr}", result.StandardError);
            }

            if (!result.Success || string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                logger?.LogWarning("headersHelper 执行失败: ExitCode={ExitCode}", result.ExitCode);
                return null;
            }

            var outputText = result.StandardOutput.Trim();
            var headers = JsonSerializer.Deserialize(outputText, McpClientJsonContext.Default.DictionaryStringJsonElement);

            if (headers == null || headers.Count == 0)
            {
                logger?.LogWarning("headersHelper 返回空结果");
                return null;
            }

            var validatedHeaders = new Dictionary<string, string>(headers.Count);
            foreach (var kvp in headers)
            {
                if (kvp.Value.ValueKind == JsonValueKind.String)
                {
                    validatedHeaders[kvp.Key] = kvp.Value.GetString() ?? string.Empty;
                }
                else
                {
                    logger?.LogWarning("headersHelper 返回非字符串值: Key={Key}, Type={Type}", kvp.Key, kvp.Value.ValueKind);
                }
            }

            logger?.LogDebug("成功获取 {Count} 个动态请求头", validatedHeaders.Count);
            return validatedHeaders;
        }
        catch (OperationCanceledException)
        {
            logger?.LogWarning("headersHelper 执行超时: {ServerName}", serverName);
            return null;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "headersHelper 执行失败: {ServerName}", serverName);
            return null;
        }
    }

    public static Dictionary<string, string> CombineHeaders(
        Dictionary<string, string>? staticHeaders,
        Dictionary<string, string>? dynamicHeaders)
    {
        var result = new Dictionary<string, string>();

        if (staticHeaders is not null)
        {
            foreach (var kvp in staticHeaders)
            {
                result[kvp.Key] = kvp.Value;
            }
        }

        if (dynamicHeaders is not null)
        {
            foreach (var kvp in dynamicHeaders)
            {
                result[kvp.Key] = kvp.Value;
            }
        }

        return result;
    }
}