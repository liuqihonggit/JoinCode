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
    private const uint ShiftPressed = 0x0010;
    private const uint LeftCtrlPressed = 0x0008;
    private const uint RightCtrlPressed = 0x0004;
    private const uint LeftAltPressed = 0x0002;
    private const uint RightAltPressed = 0x0001;
    private const uint LeftButtonDown = 0x0002;
    private const uint RightButtonDown = 0x0004;
    private const uint MouseWheeled = 0x0008;
    private const uint DoubleClick = 0x0002;

    private readonly IntPtr _handle;
    private readonly uint _originalMode;
    private bool _disposed;

    /// <summary>注入 terminalQuerier 兼容（后续 CSI 查询路由）— 当前不使用。</summary>
    public Win32InputPump()
    {
        _handle = GetStdHandle(StdInputHandle);
        GetConsoleMode(_handle, out _originalMode);
        SetConsoleMode(_handle, _originalMode | EnableMouseInput | EnableWindowInput | EnableExtendedFlags);
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
                    var mouse = DecodeMouse(record);
                    if (mouse is not null)
                    {
                        dispatch(new KeyPress(KeyKind.Mouse, string.Empty, Mouse: mouse));
                        dispatched = true;
                    }
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
    private static NativeTui.Core.Repl.MouseEvent? DecodeMouse(InputRecord record)
    {
        var kind = record.EventFlags switch
        {
            var f when (f & MouseWheeled) != 0 => (record.ButtonState & 0xFF000000) != 0
                ? MouseKind.WheelUp
                : MouseKind.WheelDown,
            _ => (record.ButtonState & (LeftButtonDown | RightButtonDown)) != 0
                ? MouseKind.Press
                : MouseKind.Release,
        };

        var button = (record.ButtonState & RightButtonDown) != 0
            ? MouseButton.Right
            : MouseButton.Left;

        return new MouseEvent(kind, button, record.MouseX, record.MouseY, false, false, false);
    }

    /// <summary>INPUT_RECORD — Win32 控制台输入记录（显式布局 union 覆盖）。</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit, Size = 20)]
    private struct InputRecord
    {
        [System.Runtime.InteropServices.FieldOffset(0)] public ushort EventType;
        [System.Runtime.InteropServices.FieldOffset(4)] public int KeyDown;
        [System.Runtime.InteropServices.FieldOffset(8)] public ushort RepeatCount;
        [System.Runtime.InteropServices.FieldOffset(10)] public ushort VirtualKeyCode;
        [System.Runtime.InteropServices.FieldOffset(14)] public char UnicodeChar;
        [System.Runtime.InteropServices.FieldOffset(16)] public uint ControlKeyState;
        [System.Runtime.InteropServices.FieldOffset(4)] public short MouseX;
        [System.Runtime.InteropServices.FieldOffset(6)] public short MouseY;
        [System.Runtime.InteropServices.FieldOffset(8)] public uint ButtonState;
        [System.Runtime.InteropServices.FieldOffset(12)] public uint EventFlags;
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
