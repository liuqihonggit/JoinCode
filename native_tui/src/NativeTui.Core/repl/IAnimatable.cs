namespace NativeTui.Core.Repl;

/// <summary>
/// 动画时钟消费者 — ReplShell 每拍（50ms tick）推进 AnimTimeMs 并广播给滚动区全部动画项
/// （对齐基准 useBlink 共享动画时钟：全部实例同相位同步闪烁）。
/// 消费方在 Render 内依据 AnimTimeMs 计算帧相位（如组头 ● 600ms 方波闪烁，基准 BLINK_INTERVAL_MS）。
/// </summary>
public interface IAnimatable
{
    /// <summary>动画时钟（50ms 帧累计，ReplShell 每帧广播注入；测试直接赋值验证确定性相位）。</summary>
    long AnimTimeMs { get; set; }
}
