namespace JoinCode.Hands.Desktop.Native;

/// <summary>
/// 光标相关 Win32 P/Invoke — GetCursorInfo + LoadCursor（PRD E-03 异步等待感知）
/// </summary>
internal static class CursorNativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct CursorInfo
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public POINT ptScreenPos;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorInfo(ref CursorInfo pci);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

    internal const int CursorShowing = 0x00000001;
    internal const int IdcArrow = 32512;
    internal const int IdcWait = 32514;
    internal const int IdcAppstarting = 32650;
    internal const int IdcHelp = 32651;
}
