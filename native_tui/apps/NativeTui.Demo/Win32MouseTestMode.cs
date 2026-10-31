namespace NativeTui.Demo;

/// <summary>
/// Win32 ReadConsoleInput 鼠标链路 spike（--win32-mouse-test，TASK077 步骤 3）：
/// 用 ReadConsoleInput P/Invoke 直接读 INPUT_RECORD（非 Console.ReadKey / stdin ReadFile —
/// 两者架构性跳过 MOUSE_EVENT）。显示每个键盘/鼠标事件。'q' 退出。
/// 诊断价值：WT 下点击有坐标显示 = ReadConsoleInput 链路通；无显示 = P/Invoke 签名/布局有误。
/// </summary>
public static class Win32MouseTestMode
{
    public static int Run(TextWriter output)
    {
        output.Write(Csi.EraseScreen() + Csi.CursorPosition(1, 1));
        output.WriteLine("=== Win32 ReadConsoleInput 鼠标 spike ===");
        output.WriteLine("在这个窗口内点击/滚轮 — 每个事件实时显示在下方。");
        output.WriteLine("有坐标显示 = ReadConsoleInput 链路通；什么都没有 = P/Invoke 签名/布局有误。");
        output.WriteLine("按 q 退出。");
        output.WriteLine();
        output.Flush();

        var count = 0;
        using var input = new Win32ConsoleInput();
        var running = true;
        while (running)
        {
            var events = input.ReadEvents();
            foreach (var evt in events)
            {
                if (evt.Key is { } key)
                {
                    count++;
                    output.WriteLine($"  [{count:D3}] KEY  {key.Key,-12} char={key.KeyChar} mods={key.Modifiers}");
                    output.Flush();
                    if (key.KeyChar == 'q')
                    {
                        running = false;
                        break;
                    }
                }

                if (evt.Mouse is { } mouse)
                {
                    count++;
                    output.WriteLine($"  [{count:D3}] MOUSE {mouse.Kind,-10} {mouse.Button,-6} row={mouse.Row} col={mouse.Col} S={mouse.Shift} C={mouse.Ctrl} A={mouse.Meta}");
                    output.Flush();
                }
            }
        }

        output.WriteLine();
        output.WriteLine($"共捕获 {count} 个事件。");
        output.Flush();
        return 0;
    }
}
