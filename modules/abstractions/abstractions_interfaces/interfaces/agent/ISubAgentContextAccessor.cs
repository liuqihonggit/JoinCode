namespace JoinCode.Abstractions.Interfaces;

public interface ISubAgentContextAccessor {
    /// <summary>获取当前子智能体上下文。</summary>
    SubAgentContext? Current { get; }
}