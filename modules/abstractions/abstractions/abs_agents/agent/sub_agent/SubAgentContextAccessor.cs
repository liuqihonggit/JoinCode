namespace JoinCode.Abstractions.Interfaces;

[Register(typeof(ISubAgentContextAccessor), ServiceLifetime.Singleton)]
public sealed class SubAgentContextAccessor : ServiceEntity, ISubAgentContextAccessor {
    /// <summary>获取当前子代理上下文。</summary>
    public SubAgentContext? Current => SubAgentContext.Current;
}