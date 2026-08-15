using JoinCode.Abstractions.Attributes;

namespace Core.Context;

/// <summary>
/// 空响应追踪器 — 追踪工具调用后LLM空响应的连续次数
/// 与 InformationEntropyGuardian 同级，作为内核组件供 CLI 和 GUI 共享
/// 重置时机：用户输入新对话 / LLM从无声变有声
/// </summary>
[Register]
public sealed class EmptyResponseTracker : ServiceEntity, IEmptyResponseTracker
{
    private int _consecutiveEmptyCount;

    public int ConsecutiveEmptyCount => _consecutiveEmptyCount;

    public int MaxConsecutiveEmpty => 5;

    public bool RecordEmptyResponse()
    {
        _consecutiveEmptyCount++;
        return _consecutiveEmptyCount > MaxConsecutiveEmpty;
    }

    public void Reset()
    {
        if (_consecutiveEmptyCount > 0)
            Diag.WriteLine($"[EmptyResponseTracker] 重置空响应计数器: {_consecutiveEmptyCount} → 0");
        _consecutiveEmptyCount = 0;
    }

    public string BuildInterventionPrompt()
    {
        return $"<system-reminder>你是否已经完成对应的操作？系统检测到你进行了空白回复（第{_consecutiveEmptyCount}次，最多{MaxConsecutiveEmpty}次）。请根据工具执行结果继续回复用户，不要进行无声退出。</system-reminder>";
    }
}
