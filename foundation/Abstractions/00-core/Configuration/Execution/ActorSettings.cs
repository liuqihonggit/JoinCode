namespace JoinCode.Abstractions.Configuration.Execution;

/// <summary>
/// Actor 模型统一配置 — 编译队列 + 背压预设,统一到 settings.json 的 current.actor 节点。
/// <para>所有 Actor 相关配置的唯一数据源,替代硬编码静态预设。</para>
/// <para>settings.json 示例:</para>
/// <code>
/// "actor": {
///   "buildQueue": { "mode": "parallel", "workerCount": 3 },
///   "backpressure": {
///     "codingAgentTask": { "capacity": 2000, "sendTimeoutSeconds": 30 },
///     "llmGateway": { "capacity": 200, "sendTimeoutSeconds": 60 },
///     "router": { "capacity": 1000, "sendTimeoutSeconds": 10 },
///     "build": { "capacity": 100, "sendTimeoutSeconds": 60 }
///   }
/// }
/// </code>
/// </summary>
public sealed class ActorSettings
{
    /// <summary>编译队列配置</summary>
    public BuildQueueSettings BuildQueue { get; set; } = new();

    /// <summary>背压预设配置 — 四档: CodingAgentTask/LlmGateway/Router/Build</summary>
    public BackpressureSettings Backpressure { get; set; } = new();

    /// <summary>
    /// 校验配置合法性 — 配置加载时调用,非法值抛 ArgumentException 带友好提示。
    /// </summary>
    public void Validate()
    {
        BuildQueue.Validate();
        Backpressure.Validate();
    }
}

/// <summary>
/// 编译队列配置 — 模式选择 + Worker 数量。
/// </summary>
public sealed class BuildQueueSettings
{
    /// <summary>
    /// 编译队列模式: "serial"(串行,BuildQueueService)或 "parallel"(并行,BuildQueueRouter)。
    /// 默认 "serial",向后兼容。
    /// </summary>
    public string Mode { get; set; } = "serial";

    /// <summary>
    /// 并行模式 Worker 数量(仅 parallel 模式生效)。
    /// 默认 2,建议等于 CPU 核心数。最小 1,最大 16。
    /// </summary>
    public int WorkerCount { get; set; } = 2;

    /// <summary>
    /// 跨进程编译锁文件路径(可选)。
    /// serial 模式默认放在 .git/JoinCode.Build.lock。
    /// parallel 模式不需要跨进程锁(同进程内 Actor 串行化)@。
    /// </summary>
    public string? CrossProcessLockPath { get; set; }

    /// <summary>是否并行模式</summary>
    public bool IsParallel => string.Equals(Mode, "parallel", StringComparison.OrdinalIgnoreCase);

    /// <summary>校验合法性</summary>
    public void Validate()
    {
        if (Mode is not ("serial" or "parallel"))
            throw new ArgumentException(
                $"BuildQueue.Mode 必须为 'serial' 或 'parallel',当前值: '{Mode}'。" +
                "在 settings.json 的 current.actor.buildQueue.mode 中修改。", nameof(Mode));

        if (WorkerCount < 1)
            throw new ArgumentException(
                $"BuildQueue.WorkerCount 必须 >= 1,当前值: {WorkerCount}。" +
                "在 settings.json 的 current.actor.buildQueue.workerCount 中修改。", nameof(WorkerCount));

        if (WorkerCount > 16)
            throw new ArgumentException(
                $"BuildQueue.WorkerCount 建议 <= 16(CPU 核心数上限),当前值: {WorkerCount}。" +
                "在 settings.json 的 current.actor.buildQueue.workerCount 中修改。", nameof(WorkerCount));
    }
}

/// <summary>
/// 背压预设配置 — 四档,对应 ActorBackpressure 的四个静态预设。
/// </summary>
public sealed class BackpressureSettings
{
    /// <summary>Coding Agent 任务队列 — 容量 2000 + 30s 超时</summary>
    public BackpressurePreset CodingAgentTask { get; set; } = new(2000, 30);

    /// <summary>LLM Gateway — 容量 200 + 60s 超时</summary>
    public BackpressurePreset LlmGateway { get; set; } = new(200, 60);

    /// <summary>Router → Worker 分发 — 容量 1000 + 10s 超时</summary>
    public BackpressurePreset Router { get; set; } = new(1000, 10);

    /// <summary>编译队列 — 容量 100 + 60s 超时</summary>
    public BackpressurePreset Build { get; set; } = new(100, 60);

    /// <summary>校验合法性</summary>
    public void Validate()
    {
        CodingAgentTask.Validate(nameof(CodingAgentTask));
        LlmGateway.Validate(nameof(LlmGateway));
        Router.Validate(nameof(Router));
        Build.Validate(nameof(Build));
    }
}

/// <summary>
/// 单档背压预设 — 容量 + 水位线 + 发送超时。
/// </summary>
public sealed class BackpressurePreset
{
    /// <summary>有界通道容量(0=无界)</summary>
    public int Capacity { get; set; }

    /// <summary>高水位线(null=容量*0.8)</summary>
    public int? HighWatermark { get; set; }

    /// <summary>危险水位线(null=容量*0.95)</summary>
    public int? CriticalWatermark { get; set; }

    /// <summary>发送超时秒数(null=不超时)</summary>
    public double? SendTimeoutSeconds { get; set; }

    public BackpressurePreset() { }

    public BackpressurePreset(int capacity, double? sendTimeoutSeconds = null)
    {
        Capacity = capacity;
        SendTimeoutSeconds = sendTimeoutSeconds;
    }

    /// <summary>校验合法性</summary>
    public void Validate(string presetName)
    {
        if (Capacity < 0)
            throw new ArgumentException(
                $"Backpressure.{presetName}.Capacity 必须 >= 0(0=无界),当前值: {Capacity}。" +
                $"在 settings.json 的 current.actor.backpressure.{presetName.ToLowerInvariant()}.capacity 中修改。",
                nameof(Capacity));

        if (HighWatermark is { } hw && hw > Capacity)
            throw new ArgumentException(
                $"Backpressure.{presetName}.HighWatermark({hw}) 不能超过 Capacity({Capacity})。" +
                $"在 settings.json 的 current.actor.backpressure.{presetName.ToLowerInvariant()}.highWatermark 中修改。",
                nameof(HighWatermark));

        if (CriticalWatermark is { } cw && cw > Capacity)
            throw new ArgumentException(
                $"Backpressure.{presetName}.CriticalWatermark({cw}) 不能超过 Capacity({Capacity})。" +
                $"在 settings.json 的 current.actor.backpressure.{presetName.ToLowerInvariant()}.criticalWatermark 中修改。",
                nameof(CriticalWatermark));

        if (SendTimeoutSeconds is { } st && st <= 0)
            throw new ArgumentException(
                $"Backpressure.{presetName}.SendTimeoutSeconds 必须 > 0,当前值: {st}。" +
                $"在 settings.json 的 current.actor.backpressure.{presetName.ToLowerInvariant()}.sendTimeoutSeconds 中修改。",
                nameof(SendTimeoutSeconds));
    }
}
