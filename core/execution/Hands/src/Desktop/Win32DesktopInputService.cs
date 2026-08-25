namespace JoinCode.Hands.Desktop;

/// <summary>
/// 桌面输入模拟服务 — Win32 SendInput/SetCursorPos 封装
/// </summary>
[Register(typeof(IDesktopInputService), ServiceLifetime.Singleton)]
public sealed partial class Win32DesktopInputService : ServiceEntity, IDesktopInputService
{
    private readonly IDesktopSafetyChecker _safetyChecker;
    private readonly ILogger<Win32DesktopInputService>? _logger;

    public Win32DesktopInputService(
        IDesktopSafetyChecker safetyChecker,
        ILogger<Win32DesktopInputService>? logger = null)
    {
        _safetyChecker = safetyChecker;
        _logger = logger;
    }

    /// <summary>移动光标到绝对像素坐标</summary>
    public Task<DesktopOperation> MoveToAsync(int x, int y, CancellationToken cancellationToken = default)
    {
        var ok = User32NativeMethods.SetCursorPos(x, y);
        var op = BuildOperation(DesktopOperationKind.Move, x, y, succeeded: ok);
        if (!ok) _logger?.LogWarning("SetCursorPos 失败: ({X},{Y})", x, y);
        return Task.FromResult(op);
    }

    /// <summary>执行鼠标动作</summary>
    public async Task<DesktopOperation> ClickAsync(int x, int y, MouseAction action, CancellationToken cancellationToken = default)
    {
        var risk = await _safetyChecker.CheckClickAsync(x, y, cancellationToken).ConfigureAwait(false);
        if (risk == UnsafeOperationKind.DangerousCoordinate)
        {
            _logger?.LogWarning("点击坐标 ({X},{Y}) 命中危险区域，已拦截", x, y);
            return BuildOperation(DesktopOperationKind.Click, x, y, mouseAction: action, succeeded: false, error: "DangerousCoordinate");
        }

        User32NativeMethods.SetCursorPos(x, y);
        var (downFlag, upFlag) = MouseActionToFlags(action);

        if (downFlag != 0) SendMouseEvent(downFlag);
        if (upFlag != 0)
        {
            await Task.Delay(ClickIntervalMs, cancellationToken).ConfigureAwait(false);
            SendMouseEvent(upFlag);
        }

        if (action == MouseAction.DoubleClick)
        {
            await Task.Delay(ClickIntervalMs, cancellationToken).ConfigureAwait(false);
            SendMouseEvent(NativeConstants.MOUSEEVENTF_LEFTDOWN);
            await Task.Delay(ClickIntervalMs, cancellationToken).ConfigureAwait(false);
            SendMouseEvent(NativeConstants.MOUSEEVENTF_LEFTUP);
        }

        return BuildOperation(DesktopOperationKind.Click, x, y, mouseAction: action, succeeded: true);
    }

    /// <summary>拖拽：按下→移动→松开</summary>
    public async Task<DesktopOperation> DragAsync(int fromX, int fromY, int toX, int toY, int? hoverMsAtTarget = null, CancellationToken cancellationToken = default)
    {
        User32NativeMethods.SetCursorPos(fromX, fromY);
        SendMouseEvent(NativeConstants.MOUSEEVENTF_LEFTDOWN);

        var steps = DragStepCount;
        for (var i = 1; i <= steps; i++)
        {
            var x = fromX + (toX - fromX) * i / steps;
            var y = fromY + (toY - fromY) * i / steps;
            User32NativeMethods.SetCursorPos(x, y);
            await Task.Delay(DragStepDelayMs, cancellationToken).ConfigureAwait(false);
        }

        if (hoverMsAtTarget is { } hover)
            await Task.Delay(hover, cancellationToken).ConfigureAwait(false);

        SendMouseEvent(NativeConstants.MOUSEEVENTF_LEFTUP);
        return BuildOperation(DesktopOperationKind.Drag, toX, toY, succeeded: true);
    }

    /// <summary>按键（单键或组合键）</summary>
    public Task<DesktopOperation> KeyPressAsync(int virtualKey, KeyModifier modifiers = KeyModifier.None, CancellationToken cancellationToken = default)
    {
        var modKeys = KeyModifierToVirtualKeys(modifiers);
        foreach (var vk in modKeys) SendKeyEvent(vk, down: true);

        SendKeyEvent((ushort)virtualKey, down: true);
        SendKeyEvent((ushort)virtualKey, down: false);

        for (var i = modKeys.Count - 1; i >= 0; i--) SendKeyEvent(modKeys[i], down: false);

        return Task.FromResult(BuildOperation(DesktopOperationKind.KeyPress, x: 0, y: 0, text: $"VK_{virtualKey:X2}", modifiers: modifiers, succeeded: true));
    }

    /// <summary>输入文本（Unicode 逐字符注入）</summary>
    public Task<DesktopOperation> TypeTextAsync(string text, CancellationToken cancellationToken = default)
    {
        foreach (var ch in text)
        {
            SendUnicodeChar(ch, down: true);
            SendUnicodeChar(ch, down: false);
        }

        return Task.FromResult(BuildOperation(DesktopOperationKind.TypeText, x: 0, y: 0, text: text, succeeded: true));
    }

    protected override void OnDispose()
    {
    }

    // ---------- 可测试的 internal static 纯方法 ----------

    /// <summary>鼠标动作转 SendInput 标志（downFlag, upFlag）</summary>
    internal static (uint downFlag, uint upFlag) MouseActionToFlags(MouseAction action) => action switch
    {
        MouseAction.Click => (NativeConstants.MOUSEEVENTF_LEFTDOWN, NativeConstants.MOUSEEVENTF_LEFTUP),
        MouseAction.RightClick => (NativeConstants.MOUSEEVENTF_RIGHTDOWN, NativeConstants.MOUSEEVENTF_RIGHTUP),
        MouseAction.DoubleClick => (NativeConstants.MOUSEEVENTF_LEFTDOWN, NativeConstants.MOUSEEVENTF_LEFTUP),
        MouseAction.MiddleClick => (NativeConstants.MOUSEEVENTF_MIDDLEDOWN, NativeConstants.MOUSEEVENTF_MIDDLEUP),
        MouseAction.LeftDown => (NativeConstants.MOUSEEVENTF_LEFTDOWN, 0u),
        MouseAction.LeftUp => (0u, NativeConstants.MOUSEEVENTF_LEFTUP),
        MouseAction.RightDown => (NativeConstants.MOUSEEVENTF_RIGHTDOWN, 0u),
        MouseAction.RightUp => (0u, NativeConstants.MOUSEEVENTF_RIGHTUP),
        MouseAction.Move => (0u, 0u),
        _ => (0u, 0u),
    };

    /// <summary>修饰键转虚拟键码序列（按下顺序：Shift→Ctrl→Alt→Win）</summary>
    internal static List<ushort> KeyModifierToVirtualKeys(KeyModifier modifiers)
    {
        var keys = new List<ushort>(4);
        if ((modifiers & KeyModifier.Shift) != 0) keys.Add(VkShift);
        if ((modifiers & KeyModifier.Control) != 0) keys.Add(VkControl);
        if ((modifiers & KeyModifier.Alt) != 0) keys.Add(VkMenu);
        if ((modifiers & KeyModifier.Win) != 0) keys.Add(VkLWin);
        return keys;
    }

    /// <summary>构造鼠标 INPUT 结构</summary>
    internal static INPUT BuildMouseInput(uint flags) => new()
    {
        type = NativeConstants.INPUT_MOUSE,
        u = new InputUnion
        {
            mi = new MOUSEINPUT { dx = 0, dy = 0, mouseData = 0, dwFlags = flags, time = 0, dwExtraInfo = IntPtr.Zero }
        }
    };

    /// <summary>构造键盘 INPUT 结构</summary>
    internal static INPUT BuildKeyInput(ushort vk, bool down) => new()
    {
        type = NativeConstants.INPUT_KEYBOARD,
        u = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = 0,
                dwFlags = down ? 0u : NativeConstants.KEYEVENTF_KEYUP,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        }
    };

    /// <summary>构造 Unicode 字符 INPUT 结构</summary>
    internal static INPUT BuildUnicodeInput(ushort scan, bool down) => new()
    {
        type = NativeConstants.INPUT_KEYBOARD,
        u = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = 0,
                wScan = scan,
                dwFlags = NativeConstants.KEYEVENTF_UNICODE | (down ? 0u : NativeConstants.KEYEVENTF_KEYUP),
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        }
    };

    // ---------- 私有发送封装 ----------

    private static void SendMouseEvent(uint flags)
    {
        var input = BuildMouseInput(flags);
        User32NativeMethods.SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    private static void SendKeyEvent(ushort vk, bool down)
    {
        var input = BuildKeyInput(vk, down);
        User32NativeMethods.SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    private static void SendUnicodeChar(char ch, bool down)
    {
        var input = BuildUnicodeInput((ushort)ch, down);
        User32NativeMethods.SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    private static DesktopOperation BuildOperation(
        DesktopOperationKind kind, int x, int y,
        string? text = null, MouseAction? mouseAction = null, KeyModifier? modifiers = null,
        bool succeeded = true, string? error = null) =>
        new(kind, x, y, text, mouseAction, modifiers, DateTimeOffset.UtcNow, succeeded, error);

    // ---------- 常量 ----------

    private const int ClickIntervalMs = 50;
    private const int DragStepCount = 20;
    private const int DragStepDelayMs = 10;
    private const ushort VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort VkMenu = 0x12;
    private const ushort VkLWin = 0x5B;
}
