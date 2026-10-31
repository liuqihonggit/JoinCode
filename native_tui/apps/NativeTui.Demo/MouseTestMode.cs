namespace NativeTui.Demo;

/// <summary>
/// 鼠标链路测试模式（--mouse-test，TASK079 用户指令"做一些 demo 让我来启动并且点击"）：
/// 启用鼠标上报（1000 点击 + 1002 拖动 + 1006 SGR）→ **原始 stdin 字节流读取**（ReadFile —
/// Console.ReadKey 只返回 KEY_EVENT 记录，鼠标 MOUSE_EVENT 记录被静默跳过 — 用户实测根因）
/// → UTF-8 解码 → KeyPressParser 解析 → 实时显示每个鼠标事件的类型与坐标。
/// 诊断价值：WT 下点击有坐标显示 = 终端→应用链路通；无显示 = 终端未发事件。'q' 退出。
/// 鼠标序列解析复用 REPL 同一解析器（KeyPressParser — 板块 C 基建 C1，主屏与 demo 同链路）。
/// </summary>
public static class MouseTestMode
{
    public static int Run(TextWriter output)
    {
        // TASK077 — 必须 RawConsoleMode 启用 ENABLE_VIRTUAL_TERMINAL_PROCESSING，
        // 否则 Windows Terminal 不识别 ?1000h 武装序列，鼠标事件不发到 stdin
        using var raw = new RawConsoleMode();
        output.Write(NativeTui.Core.Ansi.DecPrivateMode.Enable(NativeTui.Core.Ansi.DecPrivateModeKind.MouseClickTracking)
            + NativeTui.Core.Ansi.DecPrivateMode.Enable(NativeTui.Core.Ansi.DecPrivateModeKind.MouseEventTracking)
            + NativeTui.Core.Ansi.DecPrivateMode.Enable(NativeTui.Core.Ansi.DecPrivateModeKind.MouseSgrEncoding));
        output.Write(NativeTui.Core.Ansi.Csi.EraseScreen() + NativeTui.Core.Ansi.Csi.CursorPosition(1, 1));
        output.WriteLine("=== 鼠标链路测试 ===");
        output.WriteLine("在这个窗口内点击/拖动/滚轮 — 每个鼠标事件会实时显示在下方。");
        output.WriteLine("有坐标显示 = 终端→应用链路通；什么都没有 = 终端未发鼠标事件（终端不支持）。");
        output.WriteLine("按 q 退出。");
        output.WriteLine();
        output.Flush();

        var pending = new List<char>();
        var count = 0;
        var stdin = System.Console.OpenStandardInput();
        var bytes = new byte[1024];
        var decoder = System.Text.Encoding.UTF8.GetDecoder();
        var chars = new char[1024];
        var running = true;
        while (running)
        {
            var read = stdin.Read(bytes, 0, bytes.Length);
            if (read <= 0)
            {
                break;
            }

            var produced = decoder.GetChars(bytes, 0, read, chars, 0, flush: false);
            for (var i = 0; i < produced; i++)
            {
                var ch = chars[i];
                if (ch == 'q')
                {
                    running = false;
                    break;
                }

                pending.Add(ch);
            }

            // 增量解析（CA2014 — stackalloc 禁止入循环；字数上限用堆分配等价形态）
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
