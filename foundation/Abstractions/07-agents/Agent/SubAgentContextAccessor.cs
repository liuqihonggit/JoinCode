namespace JoinCode.Abstractions.Interfaces;

[Register(typeof(ISubAgentContextAccessor))]
public sealed class SubAgentContextAccessor : ServiceEntity, ISubAgentContextAccessor
{
    public SubAgentContext? Current => SubAgentContext.Current;
}
