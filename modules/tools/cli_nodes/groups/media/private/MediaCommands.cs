// media 分组本地命令表 — 只用 BCL + user32/gdi32 P/Invoke，不引用引擎 / DI / 宿主状态。
// 归属判定（2026-11-16）：能被外部调用、且实现不依赖宿主共享状态的工具下沉为独立 exe 本地实现；
// 依赖 vision LLM / 场景撤销栈 / voice-vcr 单例 / SkiaSharp 原生库的工具仍由宿主 jcc 内执行。
// 参数名与 help.txt 的 [McpToolParameter] 逐一对应；错误用 Rust 风格（原因+参数+守卫+正确写法）。
using System.Collections.Frozen;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JoinCode.Abstractions.Tools;

namespace JoinCode.CliNode.Media;

internal static partial class MediaCommands {
    private static readonly FrozenDictionary<string, CliNodeHandler> Handlers =
        new Dictionary<string, CliNodeHandler>(StringComparer.Ordinal) {
            // 进程干预（纯 BCL）
            ["list_processes"] = ListProcessesAsync,
            ["kill_process"] = KillProcessAsync,
            ["start_process"] = StartProcessAsync,
            // 输入注入（纯 user32 SendInput）
            ["mouse_click"] = MouseClickAsync,
            ["mouse_move"] = MouseMoveAsync,
            ["mouse_drag"] = MouseDragAsync,
            ["key_press"] = KeyPressAsync,
            ["type_text"] = TypeTextAsync,
            ["right_click_menu"] = RightClickMenuAsync,
            ["drag_with_hover"] = DragWithHoverAsync,
            ["multi_click"] = MultiClickAsync,
            // 窗口管理（纯 user32）
            ["list_windows"] = ListWindowsAsync,
            ["focus_window"] = FocusWindowAsync,
            ["move_window"] = MoveWindowAsync,
            ["close_window"] = CloseWindowAsync,
            // 感知与覆盖层（纯 user32/GDI/BCL）
            ["wait_for_idle"] = WaitForIdleAsync,
            ["show_desktop_overlay"] = ShowDesktopOverlayAsync,
            ["show_desktop_pulse"] = ShowDesktopPulseAsync,
            ["desktop_scene_status"] = DesktopSceneStatusAsync,
            ["list_macros"] = ListMacrosAsync,
            // 测量纯数学
            ["measure_length"] = MeasureLengthAsync,
            ["measure_ratio"] = MeasureRatioAsync,
            // 四叉树纯网格计算（无需 SkiaSharp / LLM，尺寸读 PNG IHDR）
            ["quadtree_build"] = QuadtreeBuildAsync,
            ["quadtree_paint"] = QuadtreePaintAsync,
            ["quadtree_to_screen_rects"] = QuadtreeToScreenRectsAsync,
            ["quadtree_neighbor"] = QuadtreeNeighborAsync
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static CliNodeHandler? Find(string toolId) => Handlers.TryGetValue(toolId, out var handler) ? handler : null;

    public static IEnumerable<string> LocalToolIds => Handlers.Keys.OrderBy(id => id, StringComparer.Ordinal);

    // ==================== 进程干预 ====================

    private static Task<int> ListProcessesAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var filter = Get(o, "name_filter", "");
        var maxCount = Math.Clamp(GetInt(o, "max_count", 50), 1, 1000);
        var processes = Process.GetProcesses();
        try {
            var lines = processes
                .Where(p => filter.Length == 0 || SafeName(p).Contains(filter, StringComparison.OrdinalIgnoreCase))
                .Take(maxCount)
                .Select(p => $"  PID={p.Id} {SafeName(p)}" + (Title(p) is { Length: > 0 } t ? $" [{t}]" : ""))
                .ToList();
            Console.Out.WriteLine($"共 {lines.Count} 个进程" + (filter.Length > 0 ? $"（过滤: {filter}）" : "") + ":");
            foreach (var line in lines) Console.Out.WriteLine(line);
        } finally {
            foreach (var p in processes) p.Dispose();
        }
        return Task.FromResult(0);
    }

    private static Task<int> KillProcessAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var pidText = Get(o, "pid", "");
        var name = Get(o, "name", "");
        var force = GetBool(o, "force", true);
        if (pidText.Length == 0 && name.Length == 0)
            throw new ArgumentException("kill_process 必须提供 --pid 或 --name（二者至少其一）。正确写法: jcc-tool-media.exe kill_process --pid 1234 或 --name notepad");

        Process[] targets;
        if (pidText.Length > 0) {
            if (!int.TryParse(pidText, out var pid) || pid <= 0)
                throw new ArgumentException($"kill_process 的 --pid 必须是正整数，收到 '{pidText}'。正确写法: --pid 1234");
            try { targets = [Process.GetProcessById(pid)]; }
            catch (ArgumentException) { Console.Out.WriteLine($"未找到进程 PID={pid}"); return Task.FromResult(0); }
        } else {
            targets = Process.GetProcessesByName(name);
            if (targets.Length == 0) { Console.Out.WriteLine($"未找到进程: {name}"); return Task.FromResult(0); }
        }

        try {
            foreach (var p in targets) {
                using (p) {
                    var procName = SafeName(p);
                    try {
                        if (force) p.Kill(); else p.CloseMainWindow();
                        p.WaitForExit(3000);
                        Console.Out.WriteLine($"结束进程 PID={p.Id} {procName}: {(SafeHasExited(p) ? "成功" : "超时")}");
                    } catch (Exception ex) {
                        Console.Out.WriteLine($"结束进程 PID={p.Id} {procName} 失败: {ex.Message}");
                    }
                }
            }
        } finally {
            if (pidText.Length == 0) foreach (var p in targets) p.Dispose();
        }
        return Task.FromResult(0);
    }

    private static async Task<int> StartProcessAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var fileName = Required(o, "fileName", "start_process");
        var arguments = Get(o, "arguments", "");
        var workingDir = Get(o, "workingDir", "");
        var psi = new ProcessStartInfo(fileName) { UseShellExecute = true };
        if (arguments.Length > 0) psi.Arguments = arguments;
        if (workingDir.Length > 0) {
            if (!Directory.Exists(workingDir))
                throw new ArgumentException($"start_process 的 --workingDir 不存在: {workingDir}。正确写法: --workingDir 指向已存在的目录。");
            psi.WorkingDirectory = workingDir;
        }
        using var process = Process.Start(psi);
        if (process is null) { Console.Out.WriteLine($"启动失败: {fileName}"); return 0; }
        await Task.Delay(200, ct).ConfigureAwait(false);
        Console.Out.WriteLine($"已启动: {fileName} PID={process.Id}");
        return 0;
    }

    // ==================== 输入注入 ====================

    private static async Task<int> MouseClickAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var x = RequiredInt(o, "x", "mouse_click");
        var y = RequiredInt(o, "y", "mouse_click");
        var action = Get(o, "action", "click");
        var actionText = ParseMouseAction(action);
        var (down, up) = MouseFlags(actionText);
        CheckInteractive();
        if (!User32.SetCursorPos(x, y)) Console.Error.WriteLine($"警告: SetCursorPos({x},{y}) 失败");
        if (down != 0) SendMouse(down);
        if (up != 0) { await Task.Delay(60, ct).ConfigureAwait(false); SendMouse(up); }
        if (actionText == MouseAct.DoubleClick) {
            await Task.Delay(60, ct).ConfigureAwait(false);
            SendMouse(User32.MOUSEEVENTF_LEFTDOWN);
            await Task.Delay(60, ct).ConfigureAwait(false);
            SendMouse(User32.MOUSEEVENTF_LEFTUP);
        }
        Console.Out.WriteLine($"鼠标{action} ({x},{y}): 成功");
        return 0;
    }

    private static Task<int> MouseMoveAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var x = RequiredInt(o, "x", "mouse_move");
        var y = RequiredInt(o, "y", "mouse_move");
        CheckInteractive();
        var ok = User32.SetCursorPos(x, y);
        Console.Out.WriteLine($"移动到 ({x},{y}): {(ok ? "成功" : "失败")}");
        return Task.FromResult(0);
    }

    private static async Task<int> MouseDragAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var fx = RequiredInt(o, "fromX", "mouse_drag");
        var fy = RequiredInt(o, "fromY", "mouse_drag");
        var tx = RequiredInt(o, "toX", "mouse_drag");
        var ty = RequiredInt(o, "toY", "mouse_drag");
        var hoverMs = Math.Max(0, GetInt(o, "hoverMs", 0));
        CheckInteractive();
        User32.SetCursorPos(fx, fy);
        SendMouse(User32.MOUSEEVENTF_LEFTDOWN);
        const int steps = 20;
        for (var i = 1; i <= steps; i++) {
            User32.SetCursorPos(fx + (tx - fx) * i / steps, fy + (ty - fy) * i / steps);
            await Task.Delay(8, ct).ConfigureAwait(false);
        }
        if (hoverMs > 0) await Task.Delay(hoverMs, ct).ConfigureAwait(false);
        SendMouse(User32.MOUSEEVENTF_LEFTUP);
        Console.Out.WriteLine($"拖拽 ({fx},{fy})→({tx},{ty}): 成功");
        return 0;
    }

    private static Task<int> KeyPressAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var vk = RequiredInt(o, "virtualKey", "key_press");
        var modifiers = Get(o, "modifiers", "none");
        var modKeys = ParseModifiers(modifiers);
        CheckInteractive();
        foreach (var mod in modKeys) SendKey(mod, down: true);
        SendKey((ushort)vk, down: true);
        SendKey((ushort)vk, down: false);
        for (var i = modKeys.Count - 1; i >= 0; i--) SendKey(modKeys[i], down: false);
        Console.Out.WriteLine($"按键 VK_{vk:X2}+{modifiers}: 成功");
        return Task.FromResult(0);
    }

    private static Task<int> TypeTextAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var text = Required(o, "text", "type_text");
        CheckInteractive();
        foreach (var ch in text) {
            SendUnicode(ch, down: true);
            SendUnicode(ch, down: false);
        }
        Console.Out.WriteLine($"输入文本「{text}」: 成功");
        return Task.FromResult(0);
    }

    private static async Task<int> RightClickMenuAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var tx = RequiredInt(o, "targetX", "right_click_menu");
        var ty = RequiredInt(o, "targetY", "right_click_menu");
        var mx = RequiredInt(o, "menuItemX", "right_click_menu");
        var my = RequiredInt(o, "menuItemY", "right_click_menu");
        var delay = Math.Max(0, GetInt(o, "menuRenderDelayMs", 500));
        CheckInteractive();
        User32.SetCursorPos(tx, ty);
        SendMouse(User32.MOUSEEVENTF_RIGHTDOWN);
        SendMouse(User32.MOUSEEVENTF_RIGHTUP);
        await Task.Delay(delay, ct).ConfigureAwait(false);
        User32.SetCursorPos(mx, my);
        SendMouse(User32.MOUSEEVENTF_LEFTDOWN);
        SendMouse(User32.MOUSEEVENTF_LEFTUP);
        Console.Out.WriteLine($"右键菜单 ({tx},{ty})→菜单项 ({mx},{my}): 成功");
        return 0;
    }

    private static async Task<int> DragWithHoverAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var fx = RequiredInt(o, "fromX", "drag_with_hover");
        var fy = RequiredInt(o, "fromY", "drag_with_hover");
        var tx = RequiredInt(o, "toX", "drag_with_hover");
        var ty = RequiredInt(o, "toY", "drag_with_hover");
        var hoverMs = Math.Max(0, GetInt(o, "hoverMs", 800));
        CheckInteractive();
        User32.SetCursorPos(fx, fy);
        SendMouse(User32.MOUSEEVENTF_LEFTDOWN);
        const int steps = 20;
        for (var i = 1; i <= steps; i++) {
            User32.SetCursorPos(fx + (tx - fx) * i / steps, fy + (ty - fy) * i / steps);
            await Task.Delay(8, ct).ConfigureAwait(false);
        }
        if (hoverMs > 0) await Task.Delay(hoverMs, ct).ConfigureAwait(false);
        SendMouse(User32.MOUSEEVENTF_LEFTUP);
        var popupX = GetIntOrNull(o, "popupItemX");
        var popupY = GetIntOrNull(o, "popupItemY");
        if (popupX is int px && popupY is int py) {
            User32.SetCursorPos(px, py);
            SendMouse(User32.MOUSEEVENTF_LEFTDOWN);
            SendMouse(User32.MOUSEEVENTF_LEFTUP);
        }
        Console.Out.WriteLine($"拖拽悬停 ({fx},{fy})→({tx},{ty}): 成功");
        return 0;
    }

    private static async Task<int> MultiClickAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var coordinates = Required(o, "coordinates", "multi_click");
        var stepDelay = Math.Max(0, GetInt(o, "stepDelayMs", 500));
        var points = ParseCoordinates(coordinates);
        if (points.Count == 0)
            throw new ArgumentException("multi_click 的 --coordinates 未解析出任何坐标。正确写法: --coordinates \"100,200;300,400\"");
        CheckInteractive();
        foreach (var (x, y) in points) {
            User32.SetCursorPos(x, y);
            SendMouse(User32.MOUSEEVENTF_LEFTDOWN);
            SendMouse(User32.MOUSEEVENTF_LEFTUP);
            if (stepDelay > 0) await Task.Delay(stepDelay, ct).ConfigureAwait(false);
        }
        Console.Out.WriteLine($"多点点击 {points.Count} 处: 成功");
        return 0;
    }

    // ==================== 窗口管理 ====================

    private static Task<int> ListWindowsAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var windows = EnumerateWindows();
        Console.Out.WriteLine($"共 {windows.Count} 个可见窗口:");
        foreach (var w in windows)
            Console.Out.WriteLine($"  HWND=0x{w.Handle:X} PID={w.Pid} [{w.Title}] ({w.Right - w.Left}x{w.Bottom - w.Top})");
        return Task.FromResult(0);
    }

    private static Task<int> FocusWindowAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var title = Required(o, "title", "focus_window");
        CheckInteractive();
        var win = FindWindow(title);
        if (win is null) { Console.Out.WriteLine($"未找到标题/进程名包含 '{title}' 的窗口"); return Task.FromResult(0); }
        // SendAltTap 解除 Windows 前台锁：Alt 键按下-抬起后再设置前台
        SendKey(User32.VK_MENU, down: true);
        SendKey(User32.VK_MENU, down: false);
        var ok = User32.SetForegroundWindow(win.Handle);
        Console.Out.WriteLine($"聚焦窗口 '{win.Title}': {(ok ? "成功" : "失败（Windows 前台锁）")}");
        return Task.FromResult(0);
    }

    private static Task<int> MoveWindowAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var title = Required(o, "title", "move_window");
        var x = RequiredInt(o, "x", "move_window");
        var y = RequiredInt(o, "y", "move_window");
        var width = RequiredInt(o, "width", "move_window");
        var height = RequiredInt(o, "height", "move_window");
        CheckInteractive();
        var win = FindWindow(title);
        if (win is null) { Console.Out.WriteLine($"未找到标题/进程名包含 '{title}' 的窗口"); return Task.FromResult(0); }
        var ok = User32.MoveWindow(win.Handle, x, y, width, height, true);
        Console.Out.WriteLine($"移动窗口 '{win.Title}' → ({x},{y}) {width}x{height}: {(ok ? "成功" : "失败")}");
        return Task.FromResult(0);
    }

    private static Task<int> CloseWindowAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var title = Required(o, "title", "close_window");
        CheckInteractive();
        var win = FindWindow(title);
        if (win is null) { Console.Out.WriteLine($"未找到标题/进程名包含 '{title}' 的窗口"); return Task.FromResult(0); }
        User32.PostMessage(win.Handle, User32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        Console.Out.WriteLine($"已发送关闭消息到窗口 '{win.Title}'");
        return Task.FromResult(0);
    }

    // ==================== 感知 / 覆盖层 ====================

    private static async Task<int> WaitForIdleAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var timeout = Math.Clamp(GetInt(o, "timeoutSeconds", 10), 1, 120);
        var deadline = DateTime.UtcNow.AddSeconds(timeout);
        var idleCount = 0;
        while (DateTime.UtcNow < deadline) {
            var pos1 = GetCursorPos();
            await Task.Delay(150, ct).ConfigureAwait(false);
            var pos2 = GetCursorPos();
            if (pos1 == pos2) idleCount++; else idleCount = 0;
            if (idleCount >= 4) { Console.Out.WriteLine($"桌面已空闲（光标静止 600ms）"); return 0; }
        }
        Console.Out.WriteLine($"等待 {timeout}s 结束（期间光标仍有活动）");
        return 0;
    }

    private static async Task<int> ShowDesktopOverlayAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var x = RequiredInt(o, "x", "show_desktop_overlay");
        var y = RequiredInt(o, "y", "show_desktop_overlay");
        var width = RequiredInt(o, "width", "show_desktop_overlay");
        var height = RequiredInt(o, "height", "show_desktop_overlay");
        var durationMs = Math.Max(0, GetInt(o, "durationMs", 3000));
        var color = ParseColor(Get(o, "color", "yellow"));
        CheckInteractive();
        var hdc = User32.GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero) { Console.Error.WriteLine("GetDC 失败，无法绘制覆盖层"); return 1; }
        try {
            var pen = Gdi32.CreatePen(0, 3, color);
            var nullBrush = Gdi32.GetStockObject(5); // NULL_BRUSH
            var oldPen = Gdi32.SelectObject(hdc, pen);
            var oldBrush = Gdi32.SelectObject(hdc, nullBrush);
            try {
                var deadline = DateTime.UtcNow.AddMilliseconds(durationMs);
                do {
                    Gdi32.Rectangle(hdc, x, y, x + width, y + height);
                    User32.InvalidateRect(IntPtr.Zero, IntPtr.Zero, true);
                    User32.UpdateWindow(IntPtr.Zero);
                    await Task.Delay(50, ct).ConfigureAwait(false);
                } while (DateTime.UtcNow < deadline);
            } finally {
                Gdi32.SelectObject(hdc, oldPen);
                Gdi32.SelectObject(hdc, oldBrush);
                Gdi32.DeleteObject(pen);
            }
        } finally {
            User32.ReleaseDC(IntPtr.Zero, hdc);
        }
        Console.Out.WriteLine($"覆盖层 ({x},{y}) {width}x{height} 显示 {durationMs}ms 完成");
        return 0;
    }

    private static Task<int> ListMacrosAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var directory = Get(o, "directory", "");
        if (directory.Length == 0) {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            directory = Path.Combine(appData, "JCC", "macros");
        }
        if (!Directory.Exists(directory)) { Console.Out.WriteLine($"宏目录不存在: {directory}"); return Task.FromResult(0); }
        var files = Directory.GetFiles(directory, "*.json")
            .Select(f => new FileInfo(f))
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Console.Out.WriteLine($"共 {files.Count} 个宏（目录: {directory}）:");
        foreach (var f in files) Console.Out.WriteLine($"  {f.Name} ({f.Length} 字节)");
        return Task.FromResult(0);
    }

    // ==================== 纯数学测量 ====================

    private static Task<int> MeasureLengthAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var x1 = RequiredInt(o, "x1", "measure_length");
        var y1 = RequiredInt(o, "y1", "measure_length");
        var x2 = RequiredInt(o, "x2", "measure_length");
        var y2 = RequiredInt(o, "y2", "measure_length");
        var dx = x2 - x1;
        var dy = y2 - y1;
        var length = Math.Sqrt((double)dx * dx + (double)dy * dy);
        var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        Console.Out.WriteLine($"长度: {length:F2} px，角度: {angle:F1}°");
        return Task.FromResult(0);
    }

    private static Task<int> MeasureRatioAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var width = RequiredInt(o, "width", "measure_ratio");
        var height = RequiredInt(o, "height", "measure_ratio");
        if (width <= 0 || height <= 0)
            throw new ArgumentException("measure_ratio 的 --width/--height 必须为正整数。正确写法: --width 1920 --height 1080");
        var gcd = Gcd(width, height);
        var ratio = $"{width / gcd}:{height / gcd}";
        var decimalRatio = (double)width / height;
        Console.Out.WriteLine($"宽高比: {ratio}（{decimalRatio:F4}）");
        return Task.FromResult(0);
    }

    // ==================== 桌面场景状态（只读文件，零写冲突） ====================

    private static Task<int> DesktopSceneStatusAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var sceneId = Required(o, "sceneId", "desktop_scene_status");
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jcc", "scenarios");
        var path = Path.Combine(dir, sceneId + ".json");
        if (!File.Exists(path)) {
            Console.Out.WriteLine($"场景 {sceneId} 不存在，请先调 desktop_look 创建场景。");
            return Task.FromResult(0);
        }
        SceneState? state;
        try {
            state = JsonSerializer.Deserialize(File.ReadAllText(path), SceneStateJsonContext.Default.SceneState);
        } catch (JsonException ex) {
            throw new ArgumentException($"场景文件 {path} 解析失败: {ex.Message}。正确写法: --sceneId 传入 desktop_look 创建的场景 ID");
        }
        if (state is null) {
            Console.Out.WriteLine($"场景 {sceneId} 不存在，请先调 desktop_look 创建场景。");
            return Task.FromResult(0);
        }

        // 输出格式严格对齐宿主 DesktopSceneStatusToolHandlers.StatusAsync（字节级一致）。
        var history = string.Join(", ", state.ZoomHistory.Select(h =>
            "{ \"depth\": " + h.Depth + ", \"cell\": \"" + h.CellCode + "\", \"quadrant\": " + h.Quadrant + " }"));
        Console.Out.WriteLine($$"""
            {
              "scene_id": "{{state.SceneId}}",
              "current_depth": {{state.CurrentDepth}},
              "current_cell": "{{state.CurrentCellCode}}",
              "last_action": "{{state.LastAction ?? ""}}",
              "zoom_history": [{{history}}],
              "suggested_next": [{
                "tool": "desktop_zoom",
                "reason": "继续夹逼或退层重新选象限"
              }]
            }
            """);
        return Task.FromResult(0);
    }

    // ==================== 桌面脉冲动画（主线程消息循环，无跨线程竞态） ====================

    private static Task<int> ShowDesktopPulseAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var cx = RequiredInt(o, "centerX", "show_desktop_pulse");
        var cy = RequiredInt(o, "centerY", "show_desktop_pulse");
        var maxR = GetInt(o, "maxRadius", 120);
        var minR = GetInt(o, "minRadius", 30);
        var duration = GetInt(o, "durationMs", 5000);
        var frame = GetInt(o, "frameMs", 33);
        var color = Get(o, "color", "yellow");
        var error = PulseLogic.Validate(maxR, minR, duration, frame);
        if (error is not null)
            throw new ArgumentException($"show_desktop_pulse 参数错误: {error}。正确写法: --centerX 500 --centerY 400 --maxRadius 120 --minRadius 30 --durationMs 2000");
        CheckInteractive();
        ct.ThrowIfCancellationRequested();
        var colorRef = PulseLogic.ParseColor(color);
        // 主线程直接跑消息循环：WM_TIMER 在 duration 到期自动关闭窗口并退出循环。
        // 极短 duration（测试/取消）也不会卡死——循环由窗口自己终止，不依赖外部 PostMessage 时序。
        using var overlay = new PulseOverlayWindow();
        overlay.Run(cx, cy, maxR, minR, duration, frame, colorRef);
        Console.Out.WriteLine($"桌面脉冲圆已完成: 中心({cx},{cy}) 半径{minR}-{maxR} 时长{duration}ms");
        return Task.FromResult(0);
    }

    // ==================== 四叉树纯网格计算 ====================

    private static Task<int> QuadtreeBuildAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var base64 = Required(o, "imageBase64", "quadtree_build");
        var depth = GetInt(o, "depth", 2);
        if (depth < 0) throw new ArgumentException("quadtree_build 的 --depth 不能为负。正确写法: --depth 2");
        var (w, h) = QuadtreeCodec.PngDimensions(base64);
        var grid = QuadtreeCodec.BuildGrid(w, h, depth);
        Console.Out.WriteLine(QuadtreeCodec.FormatGrid(grid, "四叉树网格构建完成"));
        return Task.FromResult(0);
    }

    private static Task<int> QuadtreePaintAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var w = RequiredInt(o, "imageWidth", "quadtree_paint");
        var h = RequiredInt(o, "imageHeight", "quadtree_paint");
        var depth = RequiredInt(o, "depth", "quadtree_paint");
        var paintsJson = Required(o, "paintsJson", "quadtree_paint");
        var paints = ParsePaints(paintsJson);
        var grid = QuadtreeCodec.PaintCells(QuadtreeCodec.BuildGrid(w, h, depth), paints);
        Console.Out.WriteLine(QuadtreeCodec.FormatGrid(grid, "染色完成"));
        return Task.FromResult(0);
    }

    private static Task<int> QuadtreeToScreenRectsAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var w = RequiredInt(o, "imageWidth", "quadtree_to_screen_rects");
        var h = RequiredInt(o, "imageHeight", "quadtree_to_screen_rects");
        var depth = RequiredInt(o, "depth", "quadtree_to_screen_rects");
        var ox = RequiredInt(o, "originScreenX", "quadtree_to_screen_rects");
        var oy = RequiredInt(o, "originScreenY", "quadtree_to_screen_rects");
        var paintsJson = Get(o, "paintsJson", "");
        QuadtreeGrid grid;
        if (paintsJson.Length > 0) {
            grid = QuadtreeCodec.PaintCells(QuadtreeCodec.BuildGrid(w, h, depth), ParsePaints(paintsJson));
        } else {
            var all = new Dictionary<string, double>(StringComparer.Ordinal);
            var baseGrid = QuadtreeCodec.BuildGrid(w, h, depth);
            foreach (var cell in baseGrid.Cells) all[cell.Code] = 0.3;
            grid = QuadtreeCodec.PaintCells(baseGrid, all);
        }
        var rects = QuadtreeCodec.MapToScreen(grid, ox, oy);
        Console.Out.WriteLine(JsonSerializer.Serialize(rects, RectJsonContext.Default.IReadOnlyListQuadtreeDesktopRect));
        return Task.FromResult(0);
    }

    private static Task<int> QuadtreeNeighborAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var cellCode = Required(o, "cellCode", "quadtree_neighbor");
        var dirText = Required(o, "direction", "quadtree_neighbor");
        var w = RequiredInt(o, "imageWidth", "quadtree_neighbor");
        var h = RequiredInt(o, "imageHeight", "quadtree_neighbor");
        var depth = RequiredInt(o, "depth", "quadtree_neighbor");
        var dir = QuadtreeCodec.ParseDirection(dirText)
            ?? throw new ArgumentException($"quadtree_neighbor 的 --direction 无效: '{dirText}'。可选: N/S/W/E/NW/NE/SW/SE");
        var neighbor = QuadtreeCodec.GetNeighbor(cellCode, dir, w, h, depth);
        Console.Out.WriteLine(neighbor is null
            ? $"格子 {cellCode} 的 {dirText} 方向邻居: 越界（不存在）"
            : $"格子 {cellCode} 的 {dirText} 方向邻居: {neighbor}");
        return Task.FromResult(0);
    }

    private static Dictionary<string, double> ParsePaints(string json) {
        try {
            var result = JsonSerializer.Deserialize(json, RectJsonContext.Default.DictionaryStringDouble);
            if (result is null || result.Count == 0)
                throw new ArgumentException("paintsJson 解析为空。正确写法: --paintsJson \"{\\\"L0.0\\\":0.5,\\\"L0.1\\\":0.8}\"");
            return result;
        } catch (JsonException ex) {
            throw new ArgumentException($"paintsJson 不是合法 JSON: {ex.Message}。正确写法: --paintsJson \"{{\\\"L0.0\\\":0.5}}\"");
        }
    }

    // System.Text.Json 源生成上下文 — quadtree_to_screen_rects 输出 DTO（ADR 0133 DTO 双向转换）。
    [JsonSerializable(typeof(Dictionary<string, double>))]
    [JsonSerializable(typeof(IReadOnlyList<QuadtreeDesktopRect>))]
    [JsonSourceGenerationOptions(WriteIndented = false)]
    private sealed partial class RectJsonContext : JsonSerializerContext;

    // ==================== 纯逻辑辅助（可打点验证） ====================

    internal enum MouseAct { Click, RightClick, DoubleClick, MiddleClick, LeftDown, LeftUp }

    internal static MouseAct ParseMouseAction(string action) => action.ToLowerInvariant() switch {
        "right_click" or "rightclick" => MouseAct.RightClick,
        "double_click" or "doubleclick" => MouseAct.DoubleClick,
        "middle" or "middle_click" or "middleclick" => MouseAct.MiddleClick,
        "left_down" or "leftdown" => MouseAct.LeftDown,
        "left_up" or "leftup" => MouseAct.LeftUp,
        _ => MouseAct.Click
    };

    internal static (uint down, uint up) MouseFlags(MouseAct act) => act switch {
        MouseAct.RightClick => (User32.MOUSEEVENTF_RIGHTDOWN, User32.MOUSEEVENTF_RIGHTUP),
        MouseAct.DoubleClick => (User32.MOUSEEVENTF_LEFTDOWN, User32.MOUSEEVENTF_LEFTUP),
        MouseAct.MiddleClick => (User32.MOUSEEVENTF_MIDDLEDOWN, User32.MOUSEEVENTF_MIDDLEUP),
        MouseAct.LeftDown => (User32.MOUSEEVENTF_LEFTDOWN, 0u),
        MouseAct.LeftUp => (0u, User32.MOUSEEVENTF_LEFTUP),
        _ => (User32.MOUSEEVENTF_LEFTDOWN, User32.MOUSEEVENTF_LEFTUP)
    };

    internal static List<ushort> ParseModifiers(string modifiers) {
        var result = new List<ushort>(4);
        var parts = modifiers.ToLowerInvariant().Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts) {
            switch (part) {
                case "shift": result.Add(User32.VK_SHIFT); break;
                case "control": case "ctrl": result.Add(User32.VK_CONTROL); break;
                case "alt": result.Add(User32.VK_MENU); break;
                case "win": case "windows": result.Add(User32.VK_LWIN); break;
            }
        }
        return result;
    }

    internal static List<(int x, int y)> ParseCoordinates(string raw) {
        var result = new List<(int, int)>();
        foreach (var pair in raw.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)) {
            var parts = pair.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out var x) && int.TryParse(parts[1], out var y))
                result.Add((x, y));
        }
        return result;
    }

    internal static int Gcd(int a, int b) { while (b != 0) (a, b) = (b, a % b); return Math.Abs(a); }

    // ==================== Win32 封装 ====================

    private static void SendMouse(uint flags) {
        var input = new INPUT { type = User32.INPUT_MOUSE, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags } } };
        User32.SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    private static void SendKey(ushort vk, bool down) {
        var input = new INPUT {
            type = User32.INPUT_KEYBOARD,
            u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = down ? 0u : User32.KEYEVENTF_KEYUP } }
        };
        User32.SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    private static void SendUnicode(char ch, bool down) {
        var input = new INPUT {
            type = User32.INPUT_KEYBOARD,
            u = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = User32.KEYEVENTF_UNICODE | (down ? 0u : User32.KEYEVENTF_KEYUP) } }
        };
        User32.SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    private static (int x, int y) GetCursorPos() => User32.GetCursorPos(out var p) ? (p.X, p.Y) : (int.MinValue, int.MinValue);

    private sealed record WinInfo(IntPtr Handle, uint Pid, string Title, int Left, int Top, int Right, int Bottom);

    private static List<WinInfo> EnumerateWindows() {
        var result = new List<WinInfo>();
        User32.EnumWindows((hWnd, _) => {
            if (!User32.IsWindowVisible(hWnd)) return true;
            var len = User32.GetWindowTextLength(hWnd);
            if (len == 0) return true;
            var sb = new StringBuilder(len + 1);
            User32.GetWindowText(hWnd, sb, sb.Capacity);
            User32.GetWindowRect(hWnd, out var rect);
            User32.GetWindowThreadProcessId(hWnd, out var pid);
            result.Add(new WinInfo(hWnd, pid, sb.ToString(), rect.Left, rect.Top, rect.Right, rect.Bottom));
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static WinInfo? FindWindow(string keyword) => EnumerateWindows()
        .FirstOrDefault(w => w.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || SafeProcessName(w.Pid).Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static string SafeProcessName(uint pid) {
        try { using var p = Process.GetProcessById((int)pid); return p.ProcessName; }
        catch { return string.Empty; }
    }

    private static void CheckInteractive() {
        // 无交互桌面时 user32 调用不会崩，只会静默失败；留此方法作为未来守卫接入点，
        // 当前不阻断（保持独立 exe 离线可用性）。
    }

    private static uint ParseColor(string name) {
        // GDI COLORREF = 0x00BBGGRR
        return name.ToLowerInvariant() switch {
            "red" => 0x000000FF,
            "green" => 0x0000FF00,
            "blue" => 0x00FF0000,
            "cyan" => 0x00FFFF00,
            "magenta" => 0x00FF00FF,
            _ => 0x0000FFFF // yellow
        };
    }

    // ==================== 参数读取（Rust 风格报错） ====================

    private static string Required(IReadOnlyDictionary<string, string> o, string key, string toolId) =>
        o.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"{toolId} 缺少必填参数 --{key}。正确写法: jcc tool call media {toolId} --{key} <值>");

    private static int RequiredInt(IReadOnlyDictionary<string, string> o, string key, string toolId) {
        var raw = Required(o, key, toolId);
        if (!int.TryParse(raw, out var value))
            throw new ArgumentException($"{toolId} 的 --{key} 必须是整数，收到 '{raw}'。正确写法: --{key} 123");
        return value;
    }

    private static string Get(IReadOnlyDictionary<string, string> o, string key, string fallback) =>
        o.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static int GetInt(IReadOnlyDictionary<string, string> o, string key, int fallback) =>
        int.TryParse(Get(o, key, string.Empty), out var value) ? value : fallback;

    private static int? GetIntOrNull(IReadOnlyDictionary<string, string> o, string key) =>
        o.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : null;

    private static bool GetBool(IReadOnlyDictionary<string, string> o, string key, bool fallback) =>
        o.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback;

    private static string SafeName(Process p) { try { return p.ProcessName; } catch { return "unknown"; } }
    private static string Title(Process p) { try { return p.MainWindowTitle; } catch { return string.Empty; } }
    private static bool SafeHasExited(Process p) { try { return p.HasExited; } catch { return false; } }
}
