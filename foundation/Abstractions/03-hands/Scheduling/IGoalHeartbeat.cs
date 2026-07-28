namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 目标心跳接口 — 管理目标引擎的活动状态追踪和定时心跳回调
/// </summary>
public interface IGoalHeartbeat : IAsyncDisposable
{
    /// <summary>当前活动引用计数</summary>
    int RefCount { get; }

    /// <summary>是否有活动正在进行</summary>
    bool IsActive { get; }

    /// <summary>最后一次活动时间</summary>
    DateTime? LastActivityAt { get; }

    /// <summary>空闲时长</summary>
    TimeSpan? IdleDuration { get; }

    /// <summary>注册心跳回调</summary>
    void RegisterCallback(Func<CancellationToken, ValueTask> callback);

    /// <summary>开始一项活动</summary>
    Task StartActivityAsync(SessionActivityReason reason);

    /// <summary>停止一项活动</summary>
    Task StopActivityAsync(SessionActivityReason reason);

    /// <summary>重置心跳状态</summary>
    Task ResetAsync();
}
