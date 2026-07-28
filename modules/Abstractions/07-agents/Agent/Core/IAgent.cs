
namespace JoinCode.Abstractions.Interfaces;

public interface IAgent
{
    Task<AgentResponse> ProcessAsync(
        string userInput,
        bool useTools = false,
        CancellationToken cancellationToken = default);

    Task ClearContextAsync(CancellationToken cancellationToken = default);

    Task<AgentContext> GetContextAsync(CancellationToken cancellationToken = default);
}
