namespace NativeTui.Core.Repl;

/// <summary>
/// 输入泵抽象（DSG042）— 键盘事件源的可切换通道（ReadKey 归一路径 / stdin 原始流 VT 全链）：
/// 生产主循环帧拍内非阻塞排空已解析键，逐键回调分派（消费方 ReplShell.HandleRawKeyPress）。
/// KeyPress 形态两通道一致（分派链零改动）；ReadKey 与原始流互斥（控制台输入缓冲独占），
/// 通道切换是构造期决策非运行时（对齐 DSG042 双通道开关渐进迁移）。
/// </summary>
public interface IInputPump
{
    /// <summary>非阻塞排空已就绪键（帧拍消费 — 无键立即返回 false）。
    /// 实现保证：分派回调内抛异常不中断排空循环的通道内部状态一致性（异常传播给调用方）。</summary>
    /// <param name="dispatch">逐键分派回调（同步 — 主循环线程）。</param>
    /// <returns>是否分派了至少一键。</returns>
    bool Drain(Action<KeyPress> dispatch);
}
