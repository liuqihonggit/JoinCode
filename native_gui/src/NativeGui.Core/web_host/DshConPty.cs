namespace JoinCode.Gui.WebHost;

/// <summary>
/// 伪终端抽象（ConPTY 的生产实现 + 单测假实现）：原始输出按 UTF-8 文本分块上报。
/// </summary>
internal interface IDshPty : IAsyncDisposable {
    /// <summary>输出块（已解码文本，保留 ANSI 控制序列）</summary>
    event Action<string>? Output;

    /// <summary>进程退出（退出码；无法取得时为 null）</summary>
    event Action<int?>? Exited;

    /// <summary>进程是否已退出</summary>
    bool HasExited { get; }

    /// <summary>写入原始输入（含控制字符）</summary>
    /// <param name="data">输入文本</param>
    void Write(string data);

    /// <summary>调整伪终端尺寸</summary>
    /// <param name="cols">列数</param>
    /// <param name="rows">行数</param>
    void Resize(int cols, int rows);
}

/// <summary>
/// Windows ConPTY 进程宿主（TASK066 C3）：创建伪控制台 + 子进程，异步读取原始输出字节流。
/// 只在 Windows 上可用；非 Windows 返回 <see cref="PlatformNotSupportedException"/>，由上层转成引导错误。
/// </summary>
internal sealed partial class DshConPty : IDshPty {
    private readonly Lock _writeGate = new();
    private readonly Process _process;
    private readonly IntPtr _pseudoConsole;
    private readonly IntPtr _attributeList;
    private readonly SafeFileHandle _inputWrite;
    private readonly SafeFileHandle _outputRead;
    private readonly Task _pump;
    private int _disposed;

    /// <inheritdoc />
    public event Action<string>? Output;

    /// <inheritdoc />
    public event Action<int?>? Exited;

    /// <inheritdoc />
    public bool HasExited => _process.HasExited;

    /// <summary>启动指定 shell</summary>
    /// <param name="shellPath">可执行文件绝对路径</param>
    /// <param name="arguments">启动参数</param>
    /// <param name="cwd">工作目录（会话工作区）</param>
    /// <param name="cols">初始列数</param>
    /// <param name="rows">初始行数</param>
    private DshConPty(string shellPath, IReadOnlyList<string> arguments, string cwd, int cols, int rows) {
        if (!OperatingSystem.IsWindows()) {
            throw new PlatformNotSupportedException("ConPTY 仅在 Windows 上可用；当前宿主平台不支持侧栏终端");
        }
        var inputRead = IntPtr.Zero;
        var outputWrite = IntPtr.Zero;
        try {
            CreateInheritablePipe(out inputRead, out var inputWrite, parentEndIsWrite: true);
            CreateInheritablePipe(out var outputRead, out outputWrite, parentEndIsWrite: false);
            var size = new COORD { X = (short)cols, Y = (short)rows };
            var hr = CreatePseudoConsole(size, inputRead, outputWrite, 0, out _pseudoConsole);
            if (hr != 0) {
                Marshal.ThrowExceptionForHR(hr);
            }
            _inputWrite = new SafeFileHandle(inputWrite, ownsHandle: true);
            _outputRead = new SafeFileHandle(outputRead, ownsHandle: true);
            (_process, _attributeList) = StartProcess(shellPath, arguments, cwd, _pseudoConsole);
        } finally {
            if (inputRead != IntPtr.Zero) {
                CloseHandle(inputRead);
            }
            if (outputWrite != IntPtr.Zero) {
                CloseHandle(outputWrite);
            }
        }
        _pump = Task.Run(PumpOutput);
    }

    /// <summary>启动一个 ConPTY 终端</summary>
    /// <param name="shellPath">shell 可执行文件</param>
    /// <param name="arguments">启动参数</param>
    /// <param name="cwd">工作目录</param>
    /// <param name="cols">列数</param>
    /// <param name="rows">行数</param>
    internal static DshConPty Start(string shellPath, IReadOnlyList<string> arguments, string cwd, int cols, int rows) =>
        new(shellPath, arguments, cwd, cols, rows);

    /// <inheritdoc />
    public unsafe void Write(string data) {
        var bytes = Encoding.UTF8.GetBytes(data);
        lock (_writeGate) {
            fixed (byte* pointer = bytes) {
                if (!WriteFile(_inputWrite, pointer, (uint)bytes.Length, out _, IntPtr.Zero)) {
                    throw new IOException($"写入终端失败（Win32 错误 {Marshal.GetLastWin32Error()}）");
                }
            }
        }
    }

    /// <inheritdoc />
    public void Resize(int cols, int rows) {
        if (ResizePseudoConsole(_pseudoConsole, new COORD { X = (short)cols, Y = (short)rows }) != 0) {
            throw new InvalidOperationException($"调整终端尺寸失败: {cols}x{rows}");
        }
    }

    /// <summary>读取输出直到管道关闭，然后上报退出码</summary>
    private unsafe void PumpOutput() {
        var decoder = Encoding.UTF8.GetDecoder();
        var bytes = new byte[8192];
        var chars = new char[8192];
        try {
            while (true) {
                int read;
                fixed (byte* pointer = bytes) {
                    if (!ReadFile(_outputRead, pointer, (uint)bytes.Length, out var readCount, IntPtr.Zero)) {
                        break;
                    }
                    read = (int)readCount;
                }
                if (read <= 0) {
                    break;
                }
                var count = decoder.GetChars(bytes, 0, read, chars, 0);
                if (count > 0) {
                    Output?.Invoke(new string(chars, 0, count));
                }
            }
        } catch (ObjectDisposedException ex) {
            GuiDiag.Log($"[DSH-TERMINAL] pty output closed: {ex.GetType().Name}");
        } catch (IOException ex) {
            GuiDiag.Log($"[DSH-TERMINAL] pty pipe broken: {ex.Message}");
        } finally {
            var exitCode = SafeExitCode();
            Exited?.Invoke(exitCode);
        }
    }

    /// <summary>取退出码（进程句柄已失效时返回 null）</summary>
    private int? SafeExitCode() {
        try {
            return _process.HasExited ? _process.ExitCode : null;
        } catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException) {
            GuiDiag.Log($"[DSH-TERMINAL] exit code unavailable: {ex.GetType().Name}");
            return null;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) {
            return;
        }
        ClosePseudoConsole(_pseudoConsole);
        try {
            if (!_process.HasExited) {
                _process.Kill(entireProcessTree: true);
            }
        } catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException) {
            GuiDiag.Log($"[DSH-TERMINAL] kill skipped: {ex.GetType().Name}");
        }
        _inputWrite.DisposeSafe();
        _outputRead.Dispose();
        if (_attributeList != IntPtr.Zero) {
            DeleteProcThreadAttributeList(_attributeList);
            Marshal.FreeHGlobal(_attributeList);
        }
        try {
            await _pump.ConfigureAwait(false);
        } catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException) {
            GuiDiag.Log($"[DSH-TERMINAL] pump ended: {ex.GetType().Name}");
        }
        _process.Dispose();
    }

    /// <summary>创建可继承的匿名管道，并去掉父进程端句柄的继承标记</summary>
    private static void CreateInheritablePipe(out IntPtr read, out IntPtr write, bool parentEndIsWrite) {
        var security = new SECURITY_ATTRIBUTES {
            nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
            bInheritHandle = 1,
        };
        if (!CreatePipe(out read, out write, ref security, 0)) {
            throw new IOException($"创建终端管道失败（Win32 错误 {Marshal.GetLastWin32Error()}）");
        }
        var parentEnd = parentEndIsWrite ? write : read;
        SetHandleInformation(parentEnd, HandleFlagInherit, 0);
    }

    /// <summary>用扩展启动信息创建挂接伪控制台的子进程</summary>
    private static (Process Process, IntPtr AttributeList) StartProcess(
        string shellPath, IReadOnlyList<string> arguments, string cwd, IntPtr pseudoConsole) {
        var size = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
        var list = Marshal.AllocHGlobal(size);
        try {
            if (!InitializeProcThreadAttributeList(list, 1, 0, ref size)) {
                throw new IOException($"初始化终端属性列表失败（Win32 错误 {Marshal.GetLastWin32Error()}）");
            }
            if (!UpdateProcThreadAttribute(list, 0, ProcThreadAttributePseudoConsole, pseudoConsole, IntPtr.Size, IntPtr.Zero, IntPtr.Zero)) {
                throw new IOException($"挂接伪控制台失败（Win32 错误 {Marshal.GetLastWin32Error()}）");
            }
            var startup = BuildStartupInfo(list);
            var commandLine = new StringBuilder(Quote(shellPath));
            foreach (var argument in arguments) {
                commandLine.Append(' ').Append(Quote(argument));
            }
            if (!CreateProcess(null, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                ExtendedStartupInfoPresent, IntPtr.Zero, cwd, ref startup, out var information)) {
                throw new IOException($"启动终端 shell 失败（Win32 错误 {Marshal.GetLastWin32Error()}）：{shellPath}");
            }
            CloseHandle(information.hThread);
            return (Process.GetProcessById(information.dwProcessId), list);
        } catch {
            DeleteProcThreadAttributeList(list);
            Marshal.FreeHGlobal(list);
            throw;
        }
    }

    /// <summary>命令行参数加引号（空参数保留空串）</summary>
    private static string Quote(string value) =>
        value.Length == 0 ? "\"\"" : value.Contains(' ') || value.Contains('"') ? $"\"{value.Replace("\"", "\\\"")}\"" : value;

    /// <summary>
    /// 构造挂接伪控制台的启动信息。标准句柄必须显式置为 INVALID_HANDLE_VALUE：
    /// Windows 在未指定 STARTF_USESTDHANDLES 时会把宿主的（可能被重定向到日志文件的）
    /// 标准句柄复制给子进程，盖过伪控制台，导致提示符写进宿主日志而 xterm 一片空白。
    /// </summary>
    /// <param name="attributeList">已挂接伪控制台的属性列表</param>
    internal static STARTUPINFOEX BuildStartupInfo(IntPtr attributeList) {
        var startup = new STARTUPINFOEX();
        startup.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
        startup.StartupInfo.dwFlags = StartfUseStdHandles;
        startup.StartupInfo.hStdInput = InvalidStdHandle;
        startup.StartupInfo.hStdOutput = InvalidStdHandle;
        startup.StartupInfo.hStdError = InvalidStdHandle;
        startup.lpAttributeList = attributeList;
        return startup;
    }

    private const int ExtendedStartupInfoPresent = 0x00080000;
    private const uint HandleFlagInherit = 0x1;
    internal const int StartfUseStdHandles = 0x00000100;
    private static readonly IntPtr ProcThreadAttributePseudoConsole = new(0x00020016);
    internal static readonly IntPtr InvalidStdHandle = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public int bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct STARTUPINFO {
        public int cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct STARTUPINFOEX {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreatePipe(out IntPtr readPipe, out IntPtr writePipe, ref SECURITY_ATTRIBUTES attributes, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetHandleInformation(IntPtr handle, uint mask, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int CreatePseudoConsole(COORD size, IntPtr input, IntPtr output, uint flags, out IntPtr pseudoConsole);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool ReadFile(
        SafeFileHandle file, byte* buffer, uint numberOfBytesToRead, out uint numberOfBytesRead, IntPtr overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool WriteFile(
        SafeFileHandle file, byte* buffer, uint numberOfBytesToWrite, out uint numberOfBytesWritten, IntPtr overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int ResizePseudoConsole(IntPtr pseudoConsole, COORD size);

    [LibraryImport("kernel32.dll")]
    private static partial void ClosePseudoConsole(IntPtr pseudoConsole);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InitializeProcThreadAttributeList(IntPtr attributeList, int attributeCount, int flags, ref IntPtr size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateProcThreadAttribute(
        IntPtr attributeList, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previousValue, IntPtr returnSize);

    [LibraryImport("kernel32.dll")]
    private static partial void DeleteProcThreadAttributeList(IntPtr attributeList);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(
        string? applicationName, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, int creationFlags, IntPtr environment,
        string? currentDirectory, ref STARTUPINFOEX startupInfo, out PROCESS_INFORMATION processInformation);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);
}
