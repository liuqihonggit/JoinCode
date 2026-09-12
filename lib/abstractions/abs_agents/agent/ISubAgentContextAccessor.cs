namespace JoinCode.Abstractions.Interfaces;

public interface ISubAgentContextAccessor
{
    SubAgentContext? Current { get; }
}
