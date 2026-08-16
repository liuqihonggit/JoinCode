namespace JoinCode.Abstractions.Pipeline;

/// <summary>
/// 中间件异常处理策略
/// </summary>
public enum ErrorBehavior
{
    /// <summary>
    /// 捕获异常，调用 onError 回调，继续执行下一个中间件
    /// 适用于非关键操作：遥测、日志、用量处理、清理、保存
    /// </summary>
    Continue,

    /// <summary>
    /// 传播异常，中断管道执行
    /// 适用于关键操作：权限检查、支付、核心业务逻辑
    /// </summary>
    Propagate
}
