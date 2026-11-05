namespace JoinCode.Abstractions.Interfaces;

public interface IMailboxPoller {
    /// <summary>开始轮询指定代理的邮箱。</summary>
    void StartPolling(string agentId, string sessionId);
    /// <summary>停止轮询指定代理的邮箱。</summary>
    void StopPolling(string agentId, string sessionId);
}