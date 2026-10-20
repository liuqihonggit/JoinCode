namespace NativeTui.Core.Repl;

/// <summary>
/// 终端尺寸源 — resize 事件化抽象（基准 useTerminalSize / Node stdout resize 事件的等价契约）：
/// ReplShell 每拍从此源取当前尺寸，变化时重建双缓冲走跨尺寸 diff（inline 增长 append / 宽度变 fullReset）。
/// 物理实现按平台替换：Windows/默认 = Console.Window* 轮询（50ms 拍内响应）；Unix 未来可接 SIGWINCH 同契约。
/// 注入面 — 测试用 fake 源确定性驱动 resize 场景（摆脱 Console 静态环境依赖）。
/// </summary>
public interface ITerminalSizeSource
{
    /// <summary>当前终端尺寸（已夹紧最小 20×8 — 超窄终端保底可渲）。</summary>
    (int Cols, int Rows) Current { get; }
}

/// <summary>Console 轮询实现（生产默认 — Console.WindowWidth/Height 每拍读取）。</summary>
public sealed class ConsoleTerminalSizeSource : ITerminalSizeSource
{
    /// <summary>当前终端尺寸（夹紧 20×8）。</summary>
    public (int Cols, int Rows) Current => (Math.Max(20, Console.WindowWidth), Math.Max(8, Console.WindowHeight));
}
