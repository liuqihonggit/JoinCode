
namespace Core.Query;

/// <summary>
/// 查询中间件共享上下文 — 承载 QueryEngine 管道各阶段需要的状态
/// 中间件通过读写此上下文协同工作，核心引擎在关键生命周期点调用钩子
/// </summary>
public sealed class QueryMiddlewareContext
{
    /// <summary>
    /// 用户输入
    /// </summary>
    public required string UserInput { get; init; }

    /// <summary>
    /// 对话历史
    /// </summary>
    public required MessageList ChatHistory { get; init; }

    /// <summary>
    /// 查询选项
    /// </summary>
    public QueryOptions? Options { get; init; }

    /// <summary>
    /// 查询引擎配置
    /// </summary>
    public required QueryEngineConfig Config { get; init; }

    /// <summary>
    /// LLM 客户端
    /// </summary>
    public required IChatClient Kernel { get; init; }

    /// <summary>
    /// 工具注册表
    /// </summary>
    public required IToolRegistry ToolRegistry { get; init; }

    /// <summary>
    /// 日志记录器
    /// </summary>
    public ILogger? Logger { get; init; }

    /// <summary>
    /// 输出块列表 — 核心引擎和中间件向此列表添加输出块
    /// </summary>
    public List<QueryStreamChunk> OutputChunks { get; } = [];

    /// <summary>
    /// 计时器
    /// </summary>
    public Stopwatch Stopwatch { get; } = Stopwatch.StartNew();

    /// <summary>
    /// 总工具调用次数
    /// </summary>
    public int TotalToolCalls { get; set; }

    /// <summary>
    /// 最近的 Token 消耗记录
    /// </summary>
    public List<TokenConsumption> RecentConsumptions { get; } = [];

    /// <summary>
    /// 当前迭代的输入 Token 数（由核心引擎设置）
    /// </summary>
    public int InputTokens { get; set; }

    /// <summary>
    /// 当前迭代的输出 Token 数（由核心引擎设置）
    /// </summary>
    public int OutputTokens { get; set; }

    /// <summary>
    /// 当前工具名称（由核心引擎设置，无工具调用时为 null）
    /// </summary>
    public string? ToolName { get; set; }

    /// <summary>
    /// 当前迭代是否有工具调用（由核心引擎设置）
    /// </summary>
    public bool HasToolCall { get; set; }

    /// <summary>
    /// 查询是否已完成（由核心引擎设置）
    /// </summary>
    public bool IsQueryComplete { get; set; }

    /// <summary>
    /// 是否应停止查询（中间件可设置此标志提前终止）
    /// </summary>
    public bool ShouldStop { get; set; }

    /// <summary>
    /// 是否应跳出循环（中间件可设置此标志跳出迭代循环）
    /// </summary>
    public bool ShouldBreak { get; set; }

    /// <summary>
    /// 总成本（美元）
    /// </summary>
    public decimal TotalCostUsd { get; set; }

    /// <summary>
    /// 缓存安全参数
    /// </summary>
    public JoinCode.Abstractions.LLM.Chat.CacheSafeParams? CacheSafeParams { get; set; }

    /// <summary>
    /// 迭代前钩子 — 中间件在 InvokeAsync 中注册，核心引擎在每次迭代开始前调用
    /// 用途: USD 预算检查等
    /// </summary>
    public List<Func<QueryMiddlewareContext, CancellationToken, Task>> BeforeIterationHooks { get; } = [];

    /// <summary>
    /// LLM 调用后钩子 — 中间件在 InvokeAsync 中注册，核心引擎在每次 LLM 调用完成后调用
    /// 用途: Token 预算消耗、成本追踪等
    /// </summary>
    public List<Func<QueryMiddlewareContext, CancellationToken, Task>> AfterLlmCallHooks { get; } = [];

    /// <summary>
    /// 工具调用后钩子 — 中间件在 InvokeAsync 中注册，核心引擎在每次工具调用完成后调用
    /// 用途: 递减回报检测、历史裁剪、空闲提醒等
    /// </summary>
    public List<Func<QueryMiddlewareContext, CancellationToken, Task>> AfterToolCallHooks { get; } = [];

    /// <summary>
    /// 查询完成钩子 — 中间件在 InvokeAsync 中注册，核心引擎在查询完成时调用
    /// 用途: 停止 Hook 执行、状态转换等
    /// </summary>
    public List<Func<QueryMiddlewareContext, CancellationToken, Task>> OnCompleteHooks { get; } = [];

    /// <summary>
    /// 内容替换服务 — 由 ContentReplacementMiddleware 设置，核心引擎在 AddToolResultToHistory 中使用
    /// 用于 MaybePersistLargeToolResult（即时持久化，非预算机制）
    /// </summary>
    public IContentReplacementService? ContentReplacementService { get; set; }
}
