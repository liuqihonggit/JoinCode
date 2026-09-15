namespace JoinCode.Hands.Desktop;

/// <summary>
/// Win32 窗口震动服务 — 通过 <c>MoveWindow</c> 震动控制台窗口，<c>FlashWindowEx</c> 闪烁任务栏。
/// </summary>
[Register(typeof(IWindowShakeService), ServiceLifetime.Singleton)]
public sealed class Win32WindowShakeService : ServiceEntity, IWindowShakeService
{
    private static readonly int[] s_offsets = { -20, 20, -16, 16, -12, 12, -8, 8, -4, 4, 0 };
    private const int StepMs = 80;
    private const int MaxParentTraversal = 16;

    private static IntPtr s_startupWindow = IntPtr.Zero;

    private readonly ILogger<Win32WindowShakeService>? _logger;

    /// <summary>
    /// 在进程启动时捕获前台窗口句柄 — 此时前台窗口最可能是 jcc 自己的终端窗口。
    /// 应在 Program 入口最早处调用。
    /// </summary>
    public static void CaptureStartupWindow()
    {
        s_startupWindow = User32NativeMethods.GetForegroundWindow();
    }

    /// <summary>
    /// 构造 Win32 窗口震动服务。
    /// </summary>
    /// <param name="logger">日志记录器（可选）。</param>
    public Win32WindowShakeService(ILogger<Win32WindowShakeService>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 震动当前进程窗口 — X 轴阻尼偏移动画 + 任务栏闪烁，总时长约 880ms。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>震动结果；null 表示无法震动。</returns>
    public async Task<ShakeResult?> ShakeWindowAsync(CancellationToken cancellationToken = default)
    {
        var (hwnd, source) = ResolveShakeableWindow();
        if (hwnd == IntPtr.Zero)
        {
            _logger?.LogWarning("无法找到可震动的窗口句柄");
            return null;
        }

        var title = GetWindowTitle(hwnd);
        FlashTaskbarCore(hwnd);

        if (!User32NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            _logger?.LogWarning("GetWindowRect 失败，无法震动");
            return null;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;

        for (var i = 0; i < s_offsets.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            User32NativeMethods.MoveWindow(hwnd, rect.Left + s_offsets[i], rect.Top, width, height, true);
            await Task.Delay(StepMs, cancellationToken).ConfigureAwait(false);
        }

        return new ShakeResult(title, $"0x{hwnd.ToInt64():X}", source);
    }

    /// <summary>
    /// 闪烁任务栏图标 — 通过 <c>FlashWindowEx</c> 闪烁 5 次。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>震动结果；null 表示无法闪烁。</returns>
    public Task<ShakeResult?> FlashTaskbarAsync(CancellationToken cancellationToken = default)
    {
        var (hwnd, source) = ResolveShakeableWindow();
        if (hwnd == IntPtr.Zero)
        {
            _logger?.LogWarning("无法找到可闪烁的窗口句柄");
            return Task.FromResult<ShakeResult?>(null);
        }

        FlashTaskbarCore(hwnd);
        var title = GetWindowTitle(hwnd);
        return Task.FromResult<ShakeResult?>(new ShakeResult(title, $"0x{hwnd.ToInt64():X}", source));
    }

    /// <summary>
    /// 解析可震动的窗口句柄 — 沿父进程链向上查找，找到第一个拥有可见顶层窗口的祖先进程。
    /// jcc.exe 作为子进程无控制台窗口，需找到父进程（终端/IDE）的窗口。
    /// </summary>
    private (IntPtr Hwnd, string Source) ResolveShakeableWindow()
    {
        var consoleHwnd = PulseNativeMethods.GetConsoleWindow();
        if (consoleHwnd != IntPtr.Zero && IsWindowShakeable(consoleHwnd))
        {
            _logger?.LogInformation("使用控制台窗口句柄 {Hwnd}", consoleHwnd);
            return (consoleHwnd, "控制台窗口");
        }

        var dirName = Path.GetFileName(Environment.CurrentDirectory);
        if (!string.IsNullOrEmpty(dirName))
        {
            var dirHwnd = FindWindowByTitleSubstring(dirName);
            if (dirHwnd != IntPtr.Zero)
            {
                var dirTitle = GetWindowTitle(dirHwnd);
                _logger?.LogInformation("按目录名\"{DirName}\"找到窗口句柄 {Hwnd} 标题=\"{Title}\"", dirName, dirHwnd, dirTitle);
                return (dirHwnd, $"目录名匹配(\"{dirName}\")");
            }
        }

        if (s_startupWindow != IntPtr.Zero && IsWindowShakeable(s_startupWindow))
        {
            var startupTitle = GetWindowTitle(s_startupWindow);
            _logger?.LogInformation("使用启动时捕获的窗口句柄 {Hwnd} 标题=\"{Title}\"", s_startupWindow, startupTitle);
            return (s_startupWindow, "启动时前台窗口");
        }

        var currentPid = (uint)Environment.ProcessId;
        var ancestorPids = GetAncestorProcessIds(currentPid, maxDepth: 8);
        var windowsByPid = EnumerateAllWindowsByPid();

        foreach (var ancestorPid in ancestorPids)
        {
            if (windowsByPid.TryGetValue(ancestorPid, out var hwnds))
            {
                var best = SelectBestWindow(hwnds);
                if (best != IntPtr.Zero)
                {
                    var title = GetWindowTitle(best);
                    _logger?.LogInformation("找到祖先进程 PID={Pid} 的窗口句柄 {Hwnd} 标题=\"{Title}\"", ancestorPid, best, title);
                    User32NativeMethods.SetForegroundWindow(best);
                    return (best, $"父进程链(PID={ancestorPid})");
                }
            }
        }

        var fg = User32NativeMethods.GetForegroundWindow();
        if (fg != IntPtr.Zero && IsWindowShakeable(fg))
        {
            _logger?.LogWarning("未找到祖先进程窗口，回退到前台窗口 {Hwnd}（沙箱环境，震动的可能不是终端窗口）", fg);
            return (fg, "前台窗口(沙箱-可能不是终端)");
        }

        _logger?.LogWarning("无法找到任何可震动窗口");
        return (IntPtr.Zero, "");
    }

    /// <summary>
    /// 获取父进程链 — 从当前进程的父进程开始，逐级向上收集祖先 PID。
    /// </summary>
    private static List<uint> GetAncestorProcessIds(uint currentPid, int maxDepth)
    {
        var result = new List<uint>();
        var snapshot = Kernel32NativeMethods.CreateToolhelp32Snapshot(Kernel32NativeMethods.TH32CS_SNAPPROCESS, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
            return result;

        try
        {
            var entry = new PROCESSENTRY32 { dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<PROCESSENTRY32>() };
            if (!Kernel32NativeMethods.Process32First(snapshot, ref entry))
                return result;

            var pidToParent = new Dictionary<uint, uint>();
            do
            {
                pidToParent[entry.th32ProcessID] = entry.th32ParentProcessID;
            }
            while (Kernel32NativeMethods.Process32Next(snapshot, ref entry));

            var pid = currentPid;
            for (var i = 0; i < maxDepth; i++)
            {
                if (!pidToParent.TryGetValue(pid, out var parentPid) || parentPid == 0 || parentPid == pid)
                    break;
                result.Add(parentPid);
                pid = parentPid;
            }
        }
        finally
        {
            Kernel32NativeMethods.CloseHandle(snapshot);
        }

        return result;
    }

    /// <summary>
    /// 枚举所有可见顶层窗口，按进程ID分组。
    /// </summary>
    private static Dictionary<uint, List<IntPtr>> EnumerateAllWindowsByPid()
    {
        var result = new Dictionary<uint, List<IntPtr>>();
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(result);
        try
        {
            User32NativeMethods.EnumWindows(static (hwnd, lParam) =>
            {
                if (!User32NativeMethods.IsWindowVisible(hwnd))
                    return true;
                User32NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
                if (pid == 0)
                    return true;
                var dict = System.Runtime.InteropServices.GCHandle.FromIntPtr(lParam).Target as Dictionary<uint, List<IntPtr>>;
                if (dict is null)
                    return true;
                if (!dict.TryGetValue(pid, out var list))
                {
                    list = [];
                    dict[pid] = list;
                }
                list.Add(hwnd);
                return true;
            }, System.Runtime.InteropServices.GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return result;
    }

    /// <summary>
    /// 获取可震动窗口的诊断信息 — 父进程链 + 每个祖先的窗口。
    /// </summary>
    public string GetWindowInfo()
    {
        var sb = new StringBuilder();
        var consoleHwnd = PulseNativeMethods.GetConsoleWindow();
        sb.AppendLine($"当前PID: {Environment.ProcessId}, ConsoleWindow句柄: 0x{consoleHwnd.ToInt64():X}");

        var currentPid = (uint)Environment.ProcessId;
        var ancestorPids = GetAncestorProcessIds(currentPid, maxDepth: 8);
        var windowsByPid = EnumerateAllWindowsByPid();

        sb.AppendLine($"父进程链: {string.Join(" → ", ancestorPids)}");
        foreach (var ancestorPid in ancestorPids)
        {
            if (windowsByPid.TryGetValue(ancestorPid, out var hwnds))
            {
                foreach (var hwnd in hwnds)
                {
                    var title = GetWindowTitle(hwnd);
                    var shakeable = IsWindowShakeable(hwnd);
                    sb.AppendLine($"  PID={ancestorPid} 句柄=0x{hwnd.ToInt64():X} 标题=\"{title}\" 可震动={shakeable}");
                }
            }
            else
            {
                sb.AppendLine($"  PID={ancestorPid} 无可见窗口");
            }
        }

        var (resolved, source) = ResolveShakeableWindow();
        sb.AppendLine($"最终选用句柄: 0x{resolved.ToInt64():X} 标题=\"{GetWindowTitle(resolved)}\" 来源={source}");
        return sb.ToString();
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var len = User32NativeMethods.GetWindowTextLength(hwnd);
        if (len <= 0) return "";
        var sb = new StringBuilder(len + 1);
        User32NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static bool IsWindowShakeable(IntPtr hwnd)
    {
        if (!User32NativeMethods.IsWindowVisible(hwnd))
        {
            return false;
        }

        if (!User32NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return false;
        }

        return rect.Right - rect.Left > 0 && rect.Bottom - rect.Top > 0;
    }

    /// <summary>
    /// 按标题子串搜索窗口 — 找到标题包含指定子串的可见顶层窗口，多个匹配选面积最大的。
    /// </summary>
    private static IntPtr FindWindowByTitleSubstring(string titleSubstring)
    {
        var ctx = new TitleSearchContext(titleSubstring);
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(ctx);
        try
        {
            User32NativeMethods.EnumWindows(static (hwnd, lParam) =>
            {
                if (!User32NativeMethods.IsWindowVisible(hwnd))
                    return true;
                var title = GetWindowTitle(hwnd);
                if (string.IsNullOrEmpty(title) || title is "Program Manager")
                    return true;
                var c = (TitleSearchContext)System.Runtime.InteropServices.GCHandle.FromIntPtr(lParam).Target!;
                if (!title.Contains(c.Substring, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (!User32NativeMethods.GetWindowRect(hwnd, out var rect))
                    return true;
                var area = (rect.Right - rect.Left) * (rect.Bottom - rect.Top);
                if (area > 0 && area > c.BestArea)
                {
                    c.BestHwnd = hwnd;
                    c.BestArea = area;
                }
                return true;
            }, System.Runtime.InteropServices.GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return ctx.BestHwnd;
    }

    private sealed class TitleSearchContext(string substring)
    {
        public readonly string Substring = substring;
        public IntPtr BestHwnd = IntPtr.Zero;
        public int BestArea;
    }

    /// <summary>
    /// 从同一进程的多个窗口中选择最佳震动目标 — 排除"Program Manager"，优先有标题且面积最大的窗口。
    /// </summary>
    private static IntPtr SelectBestWindow(List<IntPtr> hwnds)
    {
        IntPtr bestWithTitle = IntPtr.Zero;
        var bestTitleArea = 0;
        IntPtr bestAny = IntPtr.Zero;
        var bestAnyArea = 0;

        foreach (var hwnd in hwnds)
        {
            if (!IsWindowShakeable(hwnd))
                continue;

            var title = GetWindowTitle(hwnd);
            if (title is "Program Manager" or "")
                continue;

            if (!User32NativeMethods.GetWindowRect(hwnd, out var rect))
                continue;

            var area = (rect.Right - rect.Left) * (rect.Bottom - rect.Top);
            if (area <= 0)
                continue;

            if (title.Length > 0 && area > bestTitleArea)
            {
                bestWithTitle = hwnd;
                bestTitleArea = area;
            }

            if (area > bestAnyArea)
            {
                bestAny = hwnd;
                bestAnyArea = area;
            }
        }

        return bestWithTitle != IntPtr.Zero ? bestWithTitle : bestAny;
    }

    private void FlashTaskbarCore(IntPtr hwnd)
    {
        var fi = new FLASHWINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<FLASHWINFO>(),
            hwnd = hwnd,
            dwFlags = User32NativeMethods.FLASHW_ALL,
            uCount = 5,
            dwTimeout = 0
        };
        User32NativeMethods.FlashWindowEx(ref fi);
    }
}
