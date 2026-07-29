namespace JoinCode.Abstractions.Interfaces;

public interface IMailboxPoller
{
    void StartPolling(string agentId, string sessionId);
    void StopPolling(string agentId, string sessionId);
}
