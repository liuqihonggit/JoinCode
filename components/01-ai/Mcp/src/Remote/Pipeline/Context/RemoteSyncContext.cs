namespace McpToolRegistry;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// 远程同步操作类型
/// </summary>
public enum RemoteSyncOperation
{
    Tools,
    Resources,
    Prompts
}

/// <summary>
/// 远程同步管道共享上下文 — 在中间件各阶段间传递状态
/// </summary>
public sealed class RemoteSyncContext : IPipelineContext
{
    // === 输入 ===

    /// <summary>客户端 ID</summary>
    public required string ClientId { get; init; }

    /// <summary>操作类型</summary>
    public required RemoteSyncOperation Operation { get; init; }

    /// <summary>重连接受级别</summary>
    public McpReconnectAcceptLevel AcceptLevel { get; init; } = McpReconnectAcceptLevel.IdentityOnly;

    /// <summary>取消令牌</summary>
    public CancellationToken CancellationToken { get; init; }

    // === Step 1: ClientLookupMiddleware 填充 ===

    /// <summary>远程客户端</summary>
    public IMcpClient? Client { get; set; }

    /// <summary>之前的工具规格（仅 Tools 操作）</summary>
    public List<ToolSpec>? PreviousToolSpecs { get; set; }

    // === Step 2: RemoteListMiddleware 填充 ===

    /// <summary>同步的名称列表</summary>
    public List<string> SyncedNames { get; set; } = [];

    /// <summary>工具列表原始结果（仅 Tools 操作）</summary>
    public McpListToolsResult? ToolsResult { get; set; }

    // === Step 3: DriftDetectionMiddleware 填充（仅 Tools 操作） ===

    /// <summary>漂移报告</summary>
    public ToolDriftReport? DriftReport { get; set; }

    /// <summary>重连决策结果</summary>
    public McpReconnectResult? ReconnectResult { get; set; }

    /// <summary>重连策略是否拒绝</summary>
    public bool ReconnectRejected { get; set; }

    // === 输出 ===

    /// <summary>是否成功</summary>
    public bool Success { get; set; }

    /// <summary>错误消息</summary>
    public string? ErrorMessage { get; set; }

    // === IPipelineContext ===

    bool IPipelineContext.Failed { get; set; }
    string? IPipelineContext.ErrorMessage { get; set; }
    void IPipelineContext.Fail(string message)
    {
        ((IPipelineContext)this).Failed = true;
        ((IPipelineContext)this).ErrorMessage = message;
        Success = false;
        ErrorMessage = message;
    }
}
