namespace NativeTui.Core.Repl.Input;

/// <summary>
/// 启动期输入捕获 — 基准 utils/earlyInput.ts:29-67,164-169：REPL 初始化完成前的按键
/// 缓冲并回填输入框（引擎组装耗时窗口内的按键不丢失）。
/// 非阻塞 — Console.KeyAvailable 轮询，无按键立即返回空（启动路径不等待）；
/// 读取全部 intercept（不回显 — 启动期渲染未就绪防闪烁）。
/// </summary>
public static class EarlyInputBuffer
{
    /// <summary>捕获当前已到达的全部启动期按键（读代理注入 — Console 可测替身）。
    /// readKey 参数 = intercept（true 不回显 — 启动期渲染未就绪防闪烁）。</summary>
    public static List<ConsoleKeyInfo> CaptureAvailable(Func<bool> keyAvailable, Func<bool, ConsoleKeyInfo> readKey)
    {
        var captured = new List<ConsoleKeyInfo>();
        while (keyAvailable())
        {
            captured.Add(readKey(true));
        }

        return captured;
    }
}
