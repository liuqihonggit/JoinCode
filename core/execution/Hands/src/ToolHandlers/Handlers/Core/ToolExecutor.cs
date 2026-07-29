
namespace Tools;

/// <summary>
/// 工具执行器 - 负责执行工具调用并处理结果
/// </summary>
public sealed partial class ToolExecutor
{
    private readonly IToolRegistry _toolRegistry;
    [Inject] private readonly ILogger<ToolExecutor>? _logger;

    public ToolExecutor(IToolRegistry toolRegistry, ILogger<ToolExecutor>? logger = null)
    {
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _logger = logger;
    }

    /// <summary>
    /// 执行工具调用 — 对齐 TS checkPermissionsAndCallTool
    /// </summary>
    public async Task<ToolResult> ExecuteAsync(
        string toolName,
        Dictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken = default,
        ToolProgressCallback? onProgress = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolName);
        ArgumentNullException.ThrowIfNull(arguments);

        _logger?.LogInformation("Executing tool: {ToolName}", toolName);

        try
        {
            var result = await _toolRegistry.ExecuteToolAsync(toolName, arguments, cancellationToken, onProgress).ConfigureAwait(false);

            if (result.IsError)
            {
                _logger?.LogWarning("Tool execution failed: {ToolName} - {Error}",
                    toolName,
                    result.GetTextContent());
            }
            else
            {
                _logger?.LogInformation("Tool executed successfully: {ToolName}", toolName);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("Tool execution canceled: {ToolName}", toolName);
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error executing tool: {ToolName}", toolName);
            return new ToolResult
            {
                IsError = true,
                Content = new List<ToolContent>
                {
                    new() { Type = ToolContentType.Text, Text = $"Error executing tool '{toolName}': {ex.Message}" }
                }
            };
        }
    }

    /// <summary>
    /// 批量执行多个工具调用
    /// </summary>
    public async Task<IReadOnlyList<ToolResult>> ExecuteBatchAsync(
        IEnumerable<ToolCallRequest> requests,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ToolResult>();

        foreach (var request in requests)
        {
            var result = await ExecuteAsync(request.ToolName, request.Arguments, cancellationToken).ConfigureAwait(false);
            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// 并行执行多个工具调用
    /// </summary>
    public async Task<IReadOnlyList<ToolResult>> ExecuteParallelAsync(
        IEnumerable<ToolCallRequest> requests,
        CancellationToken cancellationToken = default)
    {
        var tasks = requests.Select(async request =>
        {
            return await ExecuteAsync(request.ToolName, request.Arguments, cancellationToken).ConfigureAwait(false);
        });

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// 检查工具是否存在
    /// </summary>
    public Task<bool> ToolExistsAsync(string toolName, CancellationToken cancellationToken = default)
    {
        return _toolRegistry.ContainsToolAsync(toolName, cancellationToken);
    }

    /// <summary>
    /// 获取所有可用工具
    /// </summary>
    public Task<IReadOnlyList<ToolInfo>> GetAvailableToolsAsync(CancellationToken cancellationToken = default)
    {
        return _toolRegistry.GetAllToolInfosAsync(cancellationToken);
    }
}
