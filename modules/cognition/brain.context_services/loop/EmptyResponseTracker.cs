namespace Core.Context;

/// <summary>
/// 空响应追踪器 — 追踪工具调用后LLM空响应的连续次数
/// 与 InformationEntropyGuardian 同级，作为内核组件供 CLI 和 GUI 共享
/// 重置时机：用户输入新对话 / LLM从无声变有声
/// </summary>
[Register(typeof(IEmptyResponseTracker), ServiceLifetime.Singleton)]
public sealed class EmptyResponseTracker : ServiceEntity, IEmptyResponseTracker {
    private readonly int _maxConsecutiveEmpty;
    private int _consecutiveEmptyCount;

    /// <summary>
    /// 初始化空响应追踪器，从循环干预选项读取最大连续空响应阈值
    /// </summary>
    /// <param name="options">循环干预选项，null 时使用默认阈值 5</param>
    public EmptyResponseTracker(IOptions<LoopInterventionOptions>? options = null) {
        _maxConsecutiveEmpty = options?.Value.MaxConsecutiveEmptyResponse ?? 5;
    }

    /// <summary>
    /// 当前连续空响应次数
    /// </summary>
    public int ConsecutiveEmptyCount => _consecutiveEmptyCount;

    /// <summary>
    /// 允许的最大连续空响应次数，超过即触发干预
    /// </summary>
    public int MaxConsecutiveEmpty => _maxConsecutiveEmpty;

    /// <summary>
    /// 记录一次空响应，返回是否已超过最大阈值需触发干预
    /// </summary>
    /// <returns>超过最大连续空响应阈值时返回 true，否则 false</returns>
    public bool RecordEmptyResponse() {
        _consecutiveEmptyCount++;
        return _consecutiveEmptyCount > _maxConsecutiveEmpty;
    }

    /// <summary>
    /// 重置空响应计数器，用于用户输入新对话或 LLM 恢复有声响应时
    /// </summary>
    public void Reset() {
        if (_consecutiveEmptyCount > 0)
            Diag.WriteLine($"[EmptyResponseTracker] 重置空响应计数器: {_consecutiveEmptyCount} → 0");
        _consecutiveEmptyCount = 0;
    }

    /// <summary>
    /// 构造空响应干预提示词，提醒 LLM 根据工具执行结果继续回复用户
    /// </summary>
    /// <returns>包含当前空响应计数和阈值的 system-reminder 提示词</returns>
    public string BuildInterventionPrompt() {
        return $"<system-reminder>你是否已经完成对应的操作？系统检测到你进行了空白回复（第{_consecutiveEmptyCount}次，最多{_maxConsecutiveEmpty}次）。请根据工具执行结果继续回复用户，不要进行无声退出。</system-reminder>";
    }
}