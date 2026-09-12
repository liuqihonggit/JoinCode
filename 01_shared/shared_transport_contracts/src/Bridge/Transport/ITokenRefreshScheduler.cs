namespace JoinCode.Transport.Bridge;

/// <summary>
/// Token 刷新调度器接口 — 按 sessionId 管理定时刷新
/// </summary>
public interface ITokenRefreshScheduler : IAsyncDisposable
{
    /// <summary>
    /// 基于 JWT exp 声明调度刷新
    /// </summary>
    void Schedule(string sessionId, string token);

    /// <summary>
    /// 基于 expires_in 调度刷新
    /// </summary>
    void ScheduleFromExpiresIn(string sessionId, int expiresInSeconds);

    /// <summary>取消指定会话的刷新定时器</summary>
    void Cancel(string sessionId);

    /// <summary>取消所有刷新定时器</summary>
    void CancelAll();
}
