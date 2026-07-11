namespace JoinCode.Abstractions.Interfaces;

public interface IAgentServiceFactory
{
    Task<IAgent> CreateAsync(CancellationToken cancellationToken = default);
}
