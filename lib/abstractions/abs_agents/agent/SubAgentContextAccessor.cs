namespace JoinCode.Abstractions.Interfaces;

[Register(typeof(ISubAgentContextAccessor), ServiceLifetime.Singleton)]
public sealed class SubAgentContextAccessor : ServiceEntity, ISubAgentContextAccessor
{
    public SubAgentContext? Current => SubAgentContext.Current;
}
