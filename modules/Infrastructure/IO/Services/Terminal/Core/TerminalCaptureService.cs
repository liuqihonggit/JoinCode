using JoinCode.Abstractions.Attributes;

namespace IO.Services;

[Register]
public sealed partial class TerminalCaptureService : ITerminalCaptureService
{
    private readonly IFileSystem _fs;
    [Inject] private readonly ILogger<TerminalCaptureService>? _logger;
    [Inject] private readonly IClockService _clock;

    public TerminalCaptureService(IFileSystem fs, ILogger<TerminalCaptureService>? logger = null, IClockService? clock = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
    }

    public TerminalSnapshot CaptureScreen()
    {
        var (width, height) = GetTerminalDimensions();

        string content;
        try
        {
            content = OperatingSystem.IsWindows()
                ? CaptureWindowsScreen(width, height)
                : CaptureUnixScreen(width, height, _fs);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "终端屏幕捕获失败，返回元数据");
            content = FormatMetadataFallback(width, height);
        }

        return new TerminalSnapshot
        {
            Content = content,
            Width = width,
            Height = height,
            CapturedAt = _clock.GetUtcNow()
        };
    }

    public TerminalSnapshot? CaptureBuffer(int maxLines = 50)
    {
        var (width, height) = GetTerminalBufferDimensions();

        if (Console.IsOutputRedirected)
        {
            return null;
        }

        string content;
        try
        {
            content = OperatingSystem.IsWindows()
                ? CaptureWindowsBuffer(width, maxLines)
                : CaptureUnixBuffer(width, maxLines, _fs);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "终端缓冲区捕获失败");
            return null;
        }

        return new TerminalSnapshot
        {
            Content = content,
            Width = width,
            Height = height,
            CapturedAt = _clock.GetUtcNow()
        };
    }

    #region Dimension Helpers

    private static (int width, int height) GetTerminalDimensions()
    {
        try
        {
            if (!Console.IsOutputRedirected)
            {
                return (Console.WindowWidth, Console.WindowHeight);
            }
        }
        catch (PlatformNotSupportedException ex) { System.Diagnostics.Trace.WriteLine($"Console size query not supported: {ex.Message}"); }
        return (80, 24);
    }

    private static (int width, int height) GetTerminalBufferDimensions()
    {
        try
        {
            if (!Console.IsOutputRedirected)
            {
                return (Console.BufferWidth, Console.BufferHeight);
            }
        }
        catch (PlatformNotSupportedException ex) { System.Diagnostics.Trace.WriteLine($"Console buffer query not supported: {ex.Message}"); }
        return (80, 24);
    }

    #endregion

    #region Windows Implementation

    private static string CaptureWindowsScreen(int width, int height)
    {
        try
        {
            var handle = GetStdHandle(STD_OUTPUT_HANDLE);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                return "[无法获取控制台句柄]";
            }

            var buffer = new ConsoleCharInfo[width * height];
            var coord = new Coord(0, 0);
            var size = new Coord((short)width, (short)height);
            var rect = new SmallRect(0, 0, (short)(width - 1), (short)(height - 1));

            if (!ReadConsoleOutput(handle, buffer, size, coord, ref rect))
            {
                return "[无法读取控制台输出]";
            }

            var lines = new System.Text.StringBuilder();
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var ch = buffer[y * width + x].Char;
                    lines.Append(ch == '\0' ? ' ' : ch);
                }
                lines.AppendLine();
            }

            return lines.ToString();
        }
        catch (Exception)
        {
            return "[Windows 控制台捕获失败]";
        }
    }

    private static string CaptureWindowsBuffer(int width, int maxLines)
    {
        try
        {
            var handle = GetStdHandle(STD_OUTPUT_HANDLE);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                return "[无法获取控制台句柄]";
            }

            var bufferHeight = Console.BufferHeight;
            var startLine = Math.Max(0, bufferHeight - maxLines);
            var readLines = Math.Min(maxLines, bufferHeight);

            var buffer = new ConsoleCharInfo[width * readLines];
            var coord = new Coord(0, 0);
            var size = new Coord((short)width, (short)readLines);
            var rect = new SmallRect(0, (short)startLine, (short)(width - 1), (short)(startLine + readLines - 1));

            if (!ReadConsoleOutput(handle, buffer, size, coord, ref rect))
            {
                return "[无法读取控制台缓冲区]";
            }

            var lines = new System.Text.StringBuilder();
            for (int y = 0; y < readLines; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var ch = buffer[y * width + x].Char;
                    lines.Append(ch == '\0' ? ' ' : ch);
                }
                lines.AppendLine();
            }

            return lines.ToString();
        }
        catch (Exception)
        {
            return "[Windows 缓冲区捕获失败]";
        }
    }

    #endregion

    #region Unix Implementation

    private static string CaptureUnixScreen(int width, int height, IFileSystem fs)
    {
        var tmuxContent = TryTmuxCapture();
        if (tmuxContent != null)
        {
            return tmuxContent;
        }

        var screenContent = TryScreenCapture(fs);
        if (screenContent != null)
        {
            return screenContent;
        }

        var ansiContent = TryAnsiCapture(width, height);
        if (ansiContent != null)
        {
            return ansiContent;
        }

        return FormatMetadataFallback(width, height);
    }

    private static string CaptureUnixBuffer(int width, int maxLines, IFileSystem fs)
    {
        var tmuxContent = TryTmuxCapture(maxLines);
        if (tmuxContent != null)
        {
            return tmuxContent;
        }

        var screenContent = TryScreenCapture(fs, maxLines);
        if (screenContent != null)
        {
            return screenContent;
        }

        var ansiContent = TryAnsiCapture(width, maxLines);
        if (ansiContent != null)
        {
            return ansiContent;
        }

        return FormatMetadataFallback(width, maxLines);
    }

    private static string? TryTmuxCapture(int? historyLines = null)
    {
        try
        {
            var args = historyLines.HasValue
                ? $"capture-pane -p -J -S -{historyLines.Value}"
                : "capture-pane -p -J";

            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "tmux",
                Arguments = args,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process == null) return null;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);

            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output)
                ? output.TrimEnd()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryScreenCapture(IFileSystem fs, int? maxLines = null)
    {
        try
        {
            var tmpFile = fs.CombinePath(Path.GetTempPath(), $"jcc_screen_{Guid.NewGuid():N}.txt");

            using var hardcopyProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "screen",
                Arguments = $"-X hardcopy {tmpFile}",
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (hardcopyProcess == null) return null;
            hardcopyProcess.WaitForExit(3000);

            if (!fs.FileExists(tmpFile)) return null;

            try
            {
                var content = fs.ReadAllText(tmpFile);
                if (string.IsNullOrWhiteSpace(content)) return null;

                return maxLines.HasValue
                    ? string.Join('\n', content.Split('\n').TakeLast(maxLines.Value))
                    : content.TrimEnd();
            }
            finally
            {
                try { fs.DeleteFile(tmpFile); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"TerminalCaptureService: failed to delete temp file: {ex.Message}"); }
            }
        }
        catch
        {
            return null;
        }
    }

    private static string? TryAnsiCapture(int width, int height)
    {
        try
        {
            return CaptureViaDevTty(width, height);
        }
        catch
        {
            return null;
        }
    }

    private static string? CaptureViaDevTty(int width, int height)
    {
        var fd = open("/dev/tty", O_RDWR);
        if (fd < 0) return null;

        try
        {
            var origTermios = new Termios();
            if (tcgetattr(fd, ref origTermios) != 0) return null;

            var rawTermios = origTermios;
            rawTermios.c_lflag &= ~(ICANON | ECHO);
            rawTermios.c_cc_VMIN = 1;
            rawTermios.c_cc_VTIME = 1;

            if (tcsetattr(fd, TCSANOW, ref rawTermios) != 0) return null;

            try
            {
                var cursorPos = QueryCursorPosition(fd);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"[终端屏幕 {width}x{height}]");

                if (cursorPos != null)
                {
                    sb.AppendLine($"光标位置: 行{cursorPos.Value.row} 列{cursorPos.Value.col}");
                }

                sb.AppendLine($"终端类型: {Environment.GetEnvironmentVariable("TERM") ?? "unknown"}");
                sb.AppendLine();
                sb.AppendLine("提示: 在 tmux 或 screen 会话中运行可获得完整屏幕内容捕获");

                return sb.ToString();
            }
            finally
            {
                tcsetattr(fd, TCSANOW, ref origTermios);
            }
        }
        finally
        {
            close(fd);
        }
    }

    private static (int row, int col)? QueryCursorPosition(int fd)
    {
        var dsr = new byte[] { 0x1b, 0x5b, 0x36, 0x6e };
        write(fd, dsr, (nuint)dsr.Length);

        var buf = new byte[32];
        var totalRead = 0;
        var deadline = DateTime.UtcNow.AddMilliseconds(500);

        while (DateTime.UtcNow < deadline && totalRead < buf.Length)
        {
            var n = (int)read(fd, buf, (nuint)(buf.Length - totalRead));
            if (n <= 0) break;
            totalRead += n;

            if (totalRead > 0 && buf[totalRead - 1] == (byte)'R')
                break;
        }

        if (totalRead == 0) return null;

        var response = System.Text.Encoding.ASCII.GetString(buf, 0, totalRead);
        var match = CursorPositionRegex().Match(response);
        if (match.Success)
        {
            return (int.Parse(match.Groups[1].ValueSpan), int.Parse(match.Groups[2].ValueSpan));
        }

        return null;
    }

    private static string FormatMetadataFallback(int width, int height)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[终端屏幕 {width}x{height}]");
        sb.AppendLine($"终端类型: {Environment.GetEnvironmentVariable("TERM") ?? "unknown"}");
        sb.AppendLine($"Shell: {Environment.GetEnvironmentVariable("SHELL") ?? "unknown"}");

        try
        {
            if (!Console.IsOutputRedirected)
            {
                sb.AppendLine($"光标位置: ({Console.CursorLeft}, {Console.CursorTop})");
            }
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"TerminalCaptureService: failed to get cursor position: {ex.Message}"); }

        return sb.ToString();
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\[(\d+);(\d+)R")]
    private static partial System.Text.RegularExpressions.Regex CursorPositionRegex();

    #endregion

    #region Windows P/Invoke

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct ConsoleCharInfo
    {
        public char Char;
        public short Attributes;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Coord
    {
        public short X;
        public short Y;
        public Coord(short x, short y) { X = x; Y = y; }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct SmallRect
    {
        public short Left;
        public short Top;
        public short Right;
        public short Bottom;
        public SmallRect(short left, short top, short right, short bottom)
        { Left = left; Top = top; Right = right; Bottom = bottom; }
    }

    private const int STD_OUTPUT_HANDLE = -11;

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool ReadConsoleOutput(
        IntPtr hConsoleOutput,
        ConsoleCharInfo[] lpBuffer,
        Coord dwBufferSize,
        Coord dwBufferCoord,
        ref SmallRect lpReadRegion);

    #endregion

    #region Unix P/Invoke

    private const int O_RDWR = 2;
    private const uint ICANON = 0x00000002;
    private const uint ECHO = 0x00000008;
    private const int TCSANOW = 0;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Termios
    {
        public uint c_iflag;
        public uint c_oflag;
        public uint c_cflag;
        public uint c_lflag;
        public byte c_cc_0, c_cc_1, c_cc_2, c_cc_3;
        public byte c_cc_4, c_cc_5, c_cc_6, c_cc_7;
        public byte c_cc_8, c_cc_9, c_cc_10, c_cc_11;
        public byte c_cc_12, c_cc_13, c_cc_14, c_cc_15;
        public byte c_cc_16, c_cc_17, c_cc_18, c_cc_19;
        public byte c_cc_20, c_cc_21, c_cc_22, c_cc_23;
        public byte c_cc_24, c_cc_25, c_cc_26, c_cc_27;
        public byte c_cc_28, c_cc_29, c_cc_30, c_cc_31;
        public uint c_ispeed;
        public uint c_ospeed;

        public byte c_cc_VMIN
        {
            get => c_cc_4;
            set => c_cc_4 = value;
        }

        public byte c_cc_VTIME
        {
            get => c_cc_5;
            set => c_cc_5 = value;
        }
    }

    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern int open(string pathname, int flags);

    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern int tcgetattr(int fd, ref Termios termios_p);

    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern int tcsetattr(int fd, int optional_actions, ref Termios termios_p);

    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern nint read(int fd, byte[] buf, nuint count);

    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern nint write(int fd, byte[] buf, nuint count);

    #endregion
}
