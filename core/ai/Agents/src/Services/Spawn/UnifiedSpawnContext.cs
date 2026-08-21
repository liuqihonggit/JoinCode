namespace Core.Agents;

/// <summary>
/// 统一 Spawn 管道上下文 — 合并 AgentSpawnContext(路径A) 与 AgentSpawnCoordContext(路径B)
/// 主代理/子代理/协调层共用此上下文，通过 IsMainAgent 标志区分主代理 no-op 分支
/// </summary>
public sealed class UnifiedSpawnContext : PipelineContextBase
{
    // ═══════════════════════════════════════════════════════════
    // 输入（init）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 任务描述 — 路径 B 的 Task / 路径 A 的 Options.Description
    /// </summary>
    public required string Task { get; init; }

    /// <summary>
    /// 路径 A 原始选项（工具层，含 Role/Variant/Prompt/IsolationMode 等）
    /// 主代理/路径 B 为 null
    /// </summary>
    public AgentSpawnOptions? SpawnOptions { get; init; }

    /// <summary>
    /// 路径 B 运行层选项（含 DisplayName/WorktreePath/AllowedTools 等）
    /// 路径 A/主代理为 null
    /// </summary>
    public SubAgentOptions? SubOptions { get; init; }

    /// <summary>
    /// 取消令牌
    /// </summary>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// 显式主代理标志 — 中间件据此跳过主代理不需要的步骤
    /// </summary>
    public bool IsMainAgent { get; init; }

    /// <summary>
    /// 父会话 ID — 非空时子代理 ID 派生为 {父会话ID}-sub-{NN},可读层次化
    /// </summary>
    public string? ParentSessionId { get; init; }

    // ═══════════════════════════════════════════════════════════
    // 中间产物（set）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 解析后的 Agent 定义（DefinitionResolutionMiddleware 设置）
    /// </summary>
    public JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition? Definition { get; set; }

    /// <summary>
    /// 构建后的系统提示词（PromptBuildingMiddleware 设置）
    /// </summary>
    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>
    /// 进度追踪器
    /// </summary>
    public ProgressTracker ProgressTracker { get; } = new();

    /// <summary>
    /// 过滤后的 CacheSafeParams（ContextSetupMiddleware 设置）
    /// </summary>
    public CacheSafeParams? CacheSafeParams { get; set; }

    /// <summary>
    /// 路径 A 组装后的最终 SubAgentOptions（ContextSetupMiddleware 设置）
    /// </summary>
    public SubAgentOptions? ResolvedSubOptions { get; set; }

    // ═══════════════════════════════════════════════════════════
    // 结果（set）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Spawn 后的代理实例（合并 A.SubAgent / B.Agent）
    /// </summary>
    public IAgent? Agent { get; set; }

    /// <summary>
    /// 代理 ID 计算属性
    /// </summary>
    public string AgentId => Agent?.ObjectId.UniqueId ?? string.Empty;

    // ═══════════════════════════════════════════════════════════
    // 协调层登记（set，来自路径 B）
    // ═══════════════════════════════════════════════════════════

    /// <summary>会话 ID</summary>
    public string? SessionId { get; set; }

    /// <summary>Worktree 是否已创建</summary>
    public bool WorktreeCreated { get; set; }

    /// <summary>消息通道是否已注册</summary>
    public bool MessageRegistered { get; set; }

    /// <summary>Spawn 时间戳</summary>
    public DateTime SpawnedAt { get; set; }

    /// <summary>执行上下文</summary>
    public AgentExecutionContext? ExecutionContext { get; set; }

    /// <summary>权限路由已确保</summary>
    public bool PermissionRoutingEnsured { get; set; }

    /// <summary>Plan 审批路由已启动</summary>
    public bool PlanApprovalRoutingStarted { get; set; }

    /// <summary>Teammate Pane 已创建</summary>
    public bool TeammatePaneCreated { get; set; }
}
