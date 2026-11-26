namespace NativeTui.Core.Repl;

/// <summary>
/// Win32 ReadConsoleInput 输入泵 — 替代 ReadKeyPump（Console.ReadKey 架构性跳过
/// MOUSE_EVENT INPUT_RECORD，TASK079 用户实测鼠标点击不可达的根因）。
/// ReadConsoleInput 返回原始 INPUT_RECORD（含 KEY_EVENT + MOUSE_EVENT），
/// 键盘事件转 ConsoleKeyInfo 走既有 ToKeyPress 链，鼠标事件转 KeyPress.Mouse 走 HandleMouseEvent。
/// bracketed paste 由 KeyPressParser Paste 分支天然处理（原始字节流含 200~/201~ 标记）。
/// 仅 Windows（非 Windows 平台抛 PlatformNotSupportedException）。
/// </summary>
public sealed class Win32InputPump : IInputPump, IDisposable
{
    private const int StdInputHandle = -10;
    private const ushort EventTypeKey = 1;
    private const ushort EventTypeMouse = 2;
    private const uint EnableMouseInput = 0x0010;
    private const uint EnableWindowInput = 0x0008;
    private const uint EnableExtendedFlags = 0x0080;
    /// <summary>QuickEdit（原生选区）— 鼠标追踪时必须清掉，否则客户区内拖拽被 conhost 选区吃掉。</summary>
    private const uint EnableQuickEditMode = 0x0040;
    private const uint ShiftPressed = 0x0010;
    private const uint LeftCtrlPressed = 0x0008;
    private const uint RightCtrlPressed = 0x0004;
    private const uint LeftAltPressed = 0x0002;
    private const uint RightAltPressed = 0x0001;
    private const uint LeftButtonDown = 0x0002;
    private const uint RightButtonDown = 0x0004;
    private const uint MouseWheeled = 0x0008;
    private const uint MouseHorizontalWheeled = 0x0004;
    private const uint DoubleClick = 0x0002;

    private readonly IntPtr _handle;
    private readonly uint _originalMode;
    private bool _disposed;

    /// <summary>注入 terminalQuerier 兼容（后续 CSI 查询路由）— 当前不使用。</summary>
    public Win32InputPump()
    {
        _handle = GetStdHandle(StdInputHandle);
        GetConsoleMode(_handle, out _originalMode);
        SetConsoleMode(_handle, ComputeInputMode(_originalMode, JccFeatureFlags.MouseTrackingEnabled));
    }

    /// <summary>目标 console mode（纯函数 — 可单测）：
    /// 鼠标追踪开启时必须清 ENABLE_QUICK_EDIT_MODE（0x0040）。QuickEdit 开着时客户区内一次拖拽就被
    /// conhost 当作选区：应用收不到 MOUSE_EVENT（点击/hover 全失效），且选区期间控制台输出被阻塞 —
    /// 真机表现为 resize 拖拽后整屏黑、按键才恢复，旧帧被终端裁切还会造出"输入框与历史不同宽"的错觉。
    /// ENABLE_EXTENDED_FLAGS（0x0080）必须同时置位，否则清 QuickEdit 不生效（Win32 文档要求）。
    /// 关闭鼠标追踪（JCC_NO_MOUSE_TRACKING）时保留 QuickEdit、也不接管鼠标输入 — 交还终端原生选区/复制。</summary>
    internal static uint ComputeInputMode(uint originalMode, bool mouseTracking)
    {
        var mode = originalMode | EnableWindowInput | EnableExtendedFlags;
        return mouseTracking
            ? (mode | EnableMouseInput) & ~EnableQuickEditMode
            : mode;
    }

    /// <summary>非阻塞检查 — 输入缓冲区是否有待处理事件。</summary>
    public bool HasEvents()
    {
        GetNumberOfConsoleInputEvents(_handle, out var count);
        return count > 0;
    }

    /// <summary>排空输入事件（键盘 + 鼠标统一分派）。</summary>
    public bool Drain(Action<KeyPress> dispatch)
    {
        var dispatched = false;
        var records = new InputRecord[16];
        while (GetNumberOfConsoleInputEvents(_handle, out var pending) && pending > 0)
        {
            if (!ReadConsoleInput(_handle, records, Math.Min(records.Length, pending), out var read))
            {
                break;
            }

            for (var i = 0; i < read; i++)
            {
                var record = records[i];
                if (record.EventType == EventTypeMouse)
                {
                    dispatch(new KeyPress(KeyKind.Mouse, string.Empty, Mouse: DecodeMouse(record)));
                    dispatched = true;
                }
                else if (record.EventType == EventTypeKey && record.KeyDown != 0)
                {
                    var keyInfo = MakeKeyInfo(record);
                    if (ConsoleKeyAdapter.ToKeyPress(keyInfo) is { } key)
                    {
                        dispatch(key);
                        dispatched = true;
                    }
                }
                // key-up (KeyDown==0) 与其余 EventType 静默跳过
            }
        }

        return dispatched;
    }

    /// <summary>KEY_EVENT → ConsoleKeyInfo（VK 码直映 ConsoleKey + 修饰键提取）。</summary>
    private static System.ConsoleKeyInfo MakeKeyInfo(InputRecord record)
    {
        var ctrl = (record.ControlKeyState & (LeftCtrlPressed | RightCtrlPressed)) != 0;
        var alt = (record.ControlKeyState & (LeftAltPressed | RightAltPressed)) != 0;
        var shift = (record.ControlKeyState & ShiftPressed) != 0;
        var key = (System.ConsoleKey)record.VirtualKeyCode;
        return new System.ConsoleKeyInfo(record.UnicodeChar, key, shift, alt, ctrl);
    }

    /// <summary>MOUSE_EVENT → MouseEvent（Press/Release/Wheel 分类，位置 0-based）。</summary>
    private static NativeTui.Core.Repl.MouseEvent DecodeMouse(InputRecord record) =>
        DecodeMouse(record.EventFlags, record.MouseX, record.MouseY, record.ButtonState, record.MouseControlKeyState);

    /// <summary>鼠标记录解码（纯函数 — 可单测）。TASK109 P19：滚轮方向取 dwButtonState 的高位 word
    /// 作为**有符号**增量（Win32 语义：+120 上滚 / -120 下滚）；<paramref name="controlKeyState"/> 为
    /// dwControlKeyState（不是 dwEventFlags — 两者在记录里相差 4 字节）。</summary>
    internal static MouseEvent DecodeMouse(uint eventFlags, int mouseX, int mouseY, uint buttonState, uint controlKeyState)
    {
        var kind = eventFlags switch
        {
            var f when (f & MouseWheeled) != 0 => WheelDirection(buttonState),
            var f when (f & MouseHorizontalWheeled) != 0 => HorizontalWheelDirection(buttonState),
            var f when (f & DoubleClick) != 0 => MouseKind.Press,
            _ => (buttonState & (LeftButtonDown | RightButtonDown)) != 0
                ? MouseKind.Press
                : MouseKind.Release,
        };

        var button = (buttonState & RightButtonDown) != 0
            ? MouseButton.Right
            : MouseButton.Left;

        var shift = (controlKeyState & ShiftPressed) != 0;
        var ctrl = (controlKeyState & (LeftCtrlPressed | RightCtrlPressed)) != 0;
        var alt = (controlKeyState & (LeftAltPressed | RightAltPressed)) != 0;

        return new MouseEvent(kind, button, mouseX, mouseY, shift, alt, ctrl);
    }

    /// <summary>纵向滚轮方向 — 高 16 位有符号 delta：&gt; 0 上滚，&lt; 0 下滚（0 保守判下滚）。</summary>
    private static MouseKind WheelDirection(uint buttonState) =>
        (short)(buttonState >> 16) > 0 ? MouseKind.WheelUp : MouseKind.WheelDown;

    /// <summary>横向滚轮方向 — 同为高 16 位有符号 delta：&gt; 0 右滚，&lt; 0 左滚。</summary>
    private static MouseKind HorizontalWheelDirection(uint buttonState) =>
        (short)(buttonState >> 16) > 0 ? MouseKind.WheelRight : MouseKind.WheelLeft;

    /// <summary>INPUT_RECORD — Win32 控制台输入记录（显式布局 union 覆盖）。
    /// TASK109 P19：鼠标字段偏移必须按 MOUSE_EVENT_RECORD 文档 —
    /// dwMousePosition@4 / dwButtonState@8 / dwControlKeyState@<b>12</b> / dwEventFlags@<b>16</b>；
    /// 修复前 EventFlags 误落在 12（读到的是 dwControlKeyState，恒 0）导致滚轮被当 Release。</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit, Size = 20)]
    internal struct InputRecord
    {
        [System.Runtime.InteropServices.FieldOffset(0)] public ushort EventType;
        // KEY_EVENT union 成员（EventType == 1）
        [System.Runtime.InteropServices.FieldOffset(4)] public int KeyDown;
        [System.Runtime.InteropServices.FieldOffset(8)] public ushort RepeatCount;
        [System.Runtime.InteropServices.FieldOffset(10)] public ushort VirtualKeyCode;
        [System.Runtime.InteropServices.FieldOffset(12)] public ushort VirtualScanCode;
        [System.Runtime.InteropServices.FieldOffset(14)] public char UnicodeChar;
        [System.Runtime.InteropServices.FieldOffset(16)] public uint ControlKeyState;
        // MOUSE_EVENT union 成员（EventType == 2）
        [System.Runtime.InteropServices.FieldOffset(4)] public short MouseX;
        [System.Runtime.InteropServices.FieldOffset(6)] public short MouseY;
        [System.Runtime.InteropServices.FieldOffset(8)] public uint ButtonState;
        [System.Runtime.InteropServices.FieldOffset(12)] public uint MouseControlKeyState;
        [System.Runtime.InteropServices.FieldOffset(16)] public uint EventFlags;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern System.IntPtr GetStdHandle(int nStdHandle);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool ReadConsoleInput(System.IntPtr hConsoleInput, InputRecord[] lpBuffer, int nLength, out int lpNumberOfEventsRead);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool GetNumberOfConsoleInputEvents(System.IntPtr hConsoleInput, out int lpcNumberOfEvents);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool GetConsoleMode(System.IntPtr hConsoleHandle, out uint lpMode);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool SetConsoleMode(System.IntPtr hConsoleHandle, uint dwMode);

    /// <summary>恢复原 console mode（进程退出时调用）。</summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            SetConsoleMode(_handle, _originalMode);
            _disposed = true;
        }
    }
}
