namespace NativeTui.Core.Terminal;

/// <summary>
/// Win32 ReadConsoleInput 控制台输入读取器 — 替代 Console.ReadKey（后者架构性跳过
/// MOUSE_EVENT INPUT_RECORD 记录，TASK079 用户实测根因）。同时返回键盘与鼠标事件。
/// KEY_EVENT → ConsoleKeyInfo（VK 码映射 ConsoleKey）；MOUSE_EVENT → MouseEvent 结构体。
/// 仅 Windows（Linux 终端 stdin VT 序列天然含鼠标 — 不需要本类）。
/// </summary>
public sealed class Win32ConsoleInput : IDisposable
{
    private const int StdInputHandle = -10;
    private const ushort EventTypeKey = 1;
    private const ushort EventTypeMouse = 2;
    private const uint EnableMouseInput = 0x0010;
    private const uint EnableWindowInput = 0x0008;
    private const uint EnableExtendedFlags = 0x0080;

    private readonly IntPtr _handle;
    private readonly uint _originalMode;
    private bool _disposed;

    /// <summary>统一输入事件 — Key 非 null = 键盘事件，Mouse 非 null = 鼠标事件。</summary>
    public readonly record struct InputEvent(System.ConsoleKeyInfo? Key, NativeTui.Core.Repl.MouseEvent? Mouse);

    public Win32ConsoleInput()
    {
        _handle = GetStdHandle(StdInputHandle);
        GetConsoleMode(_handle, out _originalMode);
        SetConsoleMode(_handle, _originalMode | EnableMouseInput | EnableWindowInput | EnableExtendedFlags);
    }

    /// <summary>读取一批 INPUT_RECORD 并转换为统一事件序列（阻塞直到至少一个事件）。</summary>
    public List<InputEvent> ReadEvents()
    {
        var results = new List<InputEvent>();
        var records = new InputRecord[16];
        if (!ReadConsoleInput(_handle, records, records.Length, out var read))
        {
            return results;
        }

        for (var i = 0; i < read; i++)
        {
            ref var record = ref records[i];
            if (record.EventType == EventTypeKey)
            {
                if (record.KeyDown != 0)
                {
                    results.Add(new InputEvent(MakeKeyInfo(records[i]), null));
                }
            }
            else if (record.EventType == EventTypeMouse)
            {
                results.Add(new InputEvent(null, MakeMouseEvent(records[i])));
            }
        }

        return results;
    }

    /// <summary>KEY_EVENT → ConsoleKeyInfo（VK 码直映 ConsoleKey + 修饰键提取）。</summary>
    private static System.ConsoleKeyInfo MakeKeyInfo(InputRecord record)
    {
        var ctrl = (record.ControlKeyState & 0x000C) != 0; // LEFT_CTRL | RIGHT_CTRL
        var alt = (record.ControlKeyState & 0x0003) != 0;  // LEFT_ALT | RIGHT_ALT
        var shift = (record.ControlKeyState & 0x0010) != 0; // SHIFT_PRESSED
        var key = (System.ConsoleKey)record.VirtualKeyCode;
        return new System.ConsoleKeyInfo(record.UnicodeChar, key, shift, alt, ctrl);
    }

    /// <summary>MOUSE_EVENT → MouseEvent（位置 0-based，按钮/滚轮/拖动分类）。</summary>
    private static NativeTui.Core.Repl.MouseEvent MakeMouseEvent(InputRecord record)
    {
        const uint leftDown = 0x0002;
        const uint rightDown = 0x0004;
        const uint doubleClick = 0x0002;
        const uint mouseWheeled = 0x0008;

        var kind = record.EventFlags switch
        {
            var f when (f & mouseWheeled) != 0 => (record.ButtonState & 0xFF000000) != 0
                ? NativeTui.Core.Repl.MouseKind.WheelUp
                : NativeTui.Core.Repl.MouseKind.WheelDown,
            var f when (f & doubleClick) != 0 => NativeTui.Core.Repl.MouseKind.Press,
            _ => (record.ButtonState & (leftDown | rightDown)) != 0
                ? NativeTui.Core.Repl.MouseKind.Press
                : NativeTui.Core.Repl.MouseKind.Release,
        };

        var button = (record.ButtonState & rightDown) != 0
            ? NativeTui.Core.Repl.MouseButton.Right
            : NativeTui.Core.Repl.MouseButton.Left;

        return new NativeTui.Core.Repl.MouseEvent(kind, button, record.MouseX, record.MouseY, false, false, false);
    }

    /// <summary>INPUT_RECORD — Win32 控制台输入记录（显式布局，union 字段按偏移覆盖）。</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit, Size = 20)]
    private struct InputRecord
    {
        [System.Runtime.InteropServices.FieldOffset(0)] public ushort EventType;
        // KEY_EVENT union 成员（EventType == 1）
        [System.Runtime.InteropServices.FieldOffset(4)] public int KeyDown;
        [System.Runtime.InteropServices.FieldOffset(8)] public ushort RepeatCount;
        [System.Runtime.InteropServices.FieldOffset(10)] public ushort VirtualKeyCode;
        [System.Runtime.InteropServices.FieldOffset(14)] public char UnicodeChar;
        [System.Runtime.InteropServices.FieldOffset(16)] public uint ControlKeyState;
        // MOUSE_EVENT union 成员（EventType == 2）
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
    private static extern bool GetConsoleMode(System.IntPtr hConsoleHandle, out uint lpMode);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool SetConsoleMode(System.IntPtr hConsoleHandle, uint dwMode);

    public void Dispose()
    {
        if (!_disposed)
        {
            SetConsoleMode(_handle, _originalMode);
            _disposed = true;
        }
    }
}
