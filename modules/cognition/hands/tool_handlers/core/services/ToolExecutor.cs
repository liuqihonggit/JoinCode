
namespace Tools;

/// <summary>
/// 工具执行器 - 负责执行工具调用并处理结果
/// </summary>
public sealed partial class ToolExecutor {
    private readonly IToolRegistry _toolRegistry;
    private readonly IToolExecutionGateway _toolExecutionGateway;
    private readonly ILogger<ToolExecutor>? _logger;

    /// <summary>
    /// 构造工具执行器
    /// </summary>
    /// <param name="toolRegistry">工具注册表</param>
    /// <param name="toolExecutionGateway">工具执行网关</param>
    /// <param name="logger">可选的日志记录器</param>
    public ToolExecutor(IToolRegistry toolRegistry, IToolExecutionGateway toolExecutionGateway, ILogger<ToolExecutor>? logger = null) {
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _toolExecutionGateway = toolExecutionGateway ?? throw new ArgumentNullException(nameof(toolExecutionGateway));
        _logger = logger;
    }

    /// <summary>
    /// 执行工具调用 — 对齐 TS checkPermissionsAndCallTool
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="arguments">工具调用参数字典</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <param name="onProgress">可选的进度回调</param>
    /// <returns>工具执行结果</returns>
    public async Task<ToolResult> ExecuteAsync(
        string toolName,
        Dictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken = default,
        ToolProgressCallback? onProgress = null) {
        ArgumentException.ThrowIfNullOrEmpty(toolName);
        ArgumentNullException.ThrowIfNull(arguments);

        _logger?.LogInformation("Executing tool: {ToolName}", toolName);

        try {
            var result = await _toolExecutionGateway.ExecuteAsync(toolName, arguments, cancellationToken, onProgress).ConfigureAwait(false);

            if (result.IsError) {
                _logger?.LogWarning("Tool execution failed: {ToolName} - {Error}",
                    toolName,
                    result.GetTextContent());
            } else {
                _logger?.LogInformation("Tool executed successfully: {ToolName}", toolName);
            }

            return result;
        } catch (OperationCanceledException) {
            _logger?.LogInformation("Tool execution canceled: {ToolName}", toolName);
            throw;
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger?.LogError(ex, "Error executing tool: {ToolName}", toolName);
            return ToolExceptionDiagnosticHelper.BuildErrorResult(toolName, ex, _logger);
        }
    }

    /// <summary>
    /// 批量执行多个工具调用
    /// </summary>
    /// <param name="requests">工具调用请求集合</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>各工具调用的执行结果列表</returns>
    public async Task<IReadOnlyList<ToolResult>> ExecuteBatchAsync(
        IEnumerable<ToolCallRequest> requests,
        CancellationToken cancellationToken = default) {
        var results = new List<ToolResult>();

        foreach (var request in requests) {
            var result = await ExecuteAsync(request.ToolName, request.Arguments, cancellationToken).ConfigureAwait(false);
            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// 并行执行多个工具调用
    /// </summary>
    /// <param name="requests">工具调用请求集合</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>各工具调用的执行结果列表</returns>
    public async Task<IReadOnlyList<ToolResult>> ExecuteParallelAsync(
        IEnumerable<ToolCallRequest> requests,
        CancellationToken cancellationToken = default) {
        var tasks = requests.Select(async request => {
            return await ExecuteAsync(request.ToolName, request.Arguments, cancellationToken).ConfigureAwait(false);
        });

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// 检查工具是否存在
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>存在返回 true，否则返回 false</returns>
    public Task<bool> ToolExistsAsync(string toolName, CancellationToken cancellationToken = default) {
        return _toolRegistry.ContainsToolAsync(toolName, cancellationToken);
    }

    /// <summary>
    /// 获取所有可用工具
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>可用工具信息列表</returns>
    public Task<IReadOnlyList<ToolInfo>> GetAvailableToolsAsync(CancellationToken cancellationToken = default) {
        return _toolRegistry.GetAllToolInfosAsync(cancellationToken);
    }
}