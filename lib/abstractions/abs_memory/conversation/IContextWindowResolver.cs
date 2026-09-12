
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 上下文窗口大小解析器 — 对齐 TS getContextWindowForModel 纯函数式设计
/// 每次调用实时解析当前模型的上下文窗口大小，不缓存状态
/// </summary>
public interface IContextWindowResolver
{
    /// <summary>
    /// 解析当前模型的上下文窗口大小（token 数）
    /// 根据当前活跃模型 ID 和 Provider 动态查询
    /// </summary>
    int ResolveCurrentContextWindow();
}
