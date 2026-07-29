namespace JoinCode.Abstractions.Interfaces;

[Register(typeof(ISubAgentContextAccessor))]
public sealed class SubAgentContextAccessor : ISubAgentContextAccessor
{
    public SubAgentContext? Current => SubAgentContext.Current;
}
