
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// ChatContextManager 工厂 — 为每个 Agent 实例创建独立的上下文管理器
/// 实现在 Brain/Composition 层，Abstractions 层只定义接口
/// </summary>
public interface IChatContextManagerFactory
{
    /// <summary>
    /// 为指定会话创建独立的 IChatContextManager
    /// </summary>
    IChatContextManager Create(string sessionId);
}
