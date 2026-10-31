namespace NativeTui.Demo;

/// <summary>
/// 鼠标链路测试模式（--mouse-test，TASK079 用户指令"做一些 demo 让我来启动并且点击"）：
/// 启用鼠标上报（1000 点击 + 1002 拖动 + 1006 SGR）→ 全屏等待点击 → **实时显示每个鼠标事件的
/// 类型与坐标**。诊断价值：WT 下点击有坐标显示 = 终端→应用链路通（上层分派问题）；
/// 无任何显示 = 终端未发事件（conhost 类终端物理不支持 — 非 jcc 可解）。esc 退出。
/// 鼠标序列解析复用 REPL 同一解析器（KeyPressParser — 板块 C 基建 C1，主屏与 demo 同链路）。
/// </summary>
public static class MouseTestMode
{
    public static int Run(TextWriter output)
    {
        output.Write(NativeTui.Core.Ansi.DecPrivateMode.Enable(NativeTui.Core.Ansi.DecPrivateModeKind.MouseClickTracking)
            + NativeTui.Core.Ansi.DecPrivateMode.Enable(NativeTui.Core.Ansi.DecPrivateModeKind.MouseEventTracking)
            + NativeTui.Core.Ansi.DecPrivateMode.Enable(NativeTui.Core.Ansi.DecPrivateModeKind.MouseSgrEncoding));
        output.Write(NativeTui.Core.Ansi.Csi.EraseScreen() + NativeTui.Core.Ansi.Csi.CursorPosition(1, 1));
        output.WriteLine("=== 鼠标链路测试 ===");
        output.WriteLine("在这个窗口内点击/拖动/滚轮 — 每个鼠标事件会实时显示在下方。");
        output.WriteLine("有坐标显示 = 终端→应用链路通；什么都没有 = 终端未发鼠标事件（终端不支持）。");
        output.WriteLine("按 esc 退出。");
        output.WriteLine();
        output.Flush();

        var pending = new List<char>();
        var count = 0;
        while (true)
        {
            var key = System.Console.ReadKey(intercept: true);
            if (key.Key == System.ConsoleKey.Escape)
            {
                break;
            }

            pending.Add(key.KeyChar);
            // CA2014 — stackalloc 禁止入循环；输入量级（按键字符）用堆分配 ArrayPool 等价形态
            var bufferSpan = pending.ToArray();
            var rest = (ReadOnlySpan<char>)bufferSpan;
            while (NativeTui.Core.Repl.KeyPressParser.TryParseNext(ref rest, out var parsed))
            {
                if (parsed.Mouse is { } mouse)
                {
                    count++;
                    output.WriteLine($"  [{count:D3}] {mouse.Kind,-10} {mouse.Button,-6} row={mouse.Row} col={mouse.Col}");
                    output.Flush();
                }
            }

            pending.Clear();
            foreach (var ch in rest)
            {
                pending.Add(ch);
            }
        }

        output.Write(NativeTui.Core.Ansi.DecPrivateMode.Disable(NativeTui.Core.Ansi.DecPrivateModeKind.MouseSgrEncoding)
            + NativeTui.Core.Ansi.DecPrivateMode.Disable(NativeTui.Core.Ansi.DecPrivateModeKind.MouseEventTracking)
            + NativeTui.Core.Ansi.DecPrivateMode.Disable(NativeTui.Core.Ansi.DecPrivateModeKind.MouseClickTracking));
        output.WriteLine();
        output.WriteLine($"共捕获 {count} 个鼠标事件。");
        output.Flush();
        return 0;
    }
}
