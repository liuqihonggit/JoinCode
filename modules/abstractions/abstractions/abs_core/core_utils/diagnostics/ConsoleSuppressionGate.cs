namespace JoinCode.Abstractions.Utils.Diagnostics;

/// <summary>控制台抑制作用域 — <see cref="ConsoleSuppressionGate.Begin"/> 返回，using var 释放（Dispose 幂等）。</summary>
public sealed class ConsoleSuppressionScope : IDisposable {
    private int _disposed;

    internal ConsoleSuppressionScope() { }

    /// <summary>释放 — 最外层释放时恢复原 writers 并把环形缓冲冲刷回原 stderr。</summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) {
            return;
        }

        ConsoleSuppressionGate.End();
    }
}

/// <summary>
/// 控制台抑制闸门 — TUI 会话期独占终端画布的防御层（TASK077）：
/// Begin 换装 Console.Out/Console.Error 为环形缓冲捕获 writer，抑制期间任何组件
/// （Diag/AsyncStderrWriter/McpPluginBootstrap 等直接 Console 调用）的诊断行不再触碰终端 —
/// inline TUI 的相对光标渲染模型一旦被外部写打乱（底行溢出滚屏/列错位）整帧永久失同步；
/// 最外层 Dispose 恢复原 writers 并把缓冲行冲刷回原 stderr（CI/E2E 诊断捕获不受损）；
/// TakeSuppressedLines 供 /debuglog 类消费方排水。嵌套 Begin 引用计数，内层退出不恢复不冲刷。
/// </summary>
public static class ConsoleSuppressionGate {
    private const int RingCap = 1024;
    private static readonly object GateLock = new();
    private static readonly List<string> Ring = new();
    private static int _depth;
    private static TextWriter _originalOut = TextWriter.Null;
    private static TextWriter _originalError = TextWriter.Null;
    private static SuppressingWriter? _suppressingOut;
    private static SuppressingWriter? _suppressingError;

    /// <summary>开始抑制 — 返回 using var 作用域；嵌套调用引用计数。</summary>
    /// <param name="reason">抑制原因（如 "TUI 会话"）— 语义标注，当前不落盘。</param>
    public static ConsoleSuppressionScope Begin(string reason = "") {
        lock (GateLock) {
            _depth++;
            if (_depth == 1) {
                _originalOut = Console.Out;
                _originalError = Console.Error;
                _suppressingOut = new SuppressingWriter(Append);
                _suppressingError = new SuppressingWriter(Append);
                Console.SetOut(_suppressingOut);
                Console.SetError(_suppressingError);
            }
        }

        return new ConsoleSuppressionScope();
    }

    /// <summary>排水 — 取走当前缓冲行并清空（/debuglog 类消费方；抑制期间调用安全）。</summary>
    public static IReadOnlyList<string> TakeSuppressedLines() {
        lock (GateLock) {
            var lines = Ring.ToArray();
            Ring.Clear();
            return lines;
        }
    }

    internal static void End() {
        TextWriter? flushTarget;
        List<string> lines;
        lock (GateLock) {
            _depth--;
            if (_depth > 0) {
                return;
            }

            if (Console.Out == _suppressingOut) {
                Console.SetOut(_originalOut);
            }

            if (Console.Error == _suppressingError) {
                Console.SetError(_originalError);
            }

            lines = new List<string>(Ring);
            Ring.Clear();
            flushTarget = _originalError;
            _originalOut = TextWriter.Null;
            _originalError = TextWriter.Null;
            _suppressingOut = null;
            _suppressingError = null;
        }

        foreach (var line in lines) {
            try { flushTarget.WriteLine(line); } catch (IOException) { break; }
        }
    }

    private static void Append(string line) {
        lock (GateLock) {
            Ring.Add(line);
            if (Ring.Count > RingCap) {
                Ring.RemoveRange(0, Ring.Count - RingCap);
            }
        }
    }

    /// <summary>捕获 writer — 行缓冲累积，换行提交到环形缓冲；内嵌换行按行切分；不关闭真实控制台。</summary>
    private sealed class SuppressingWriter : TextWriter {
        private readonly Action<string> _commit;
        private readonly StringBuilder _pending = new();
        private int _disposed;

        /// <summary>创建捕获 writer — 行提交回调指向闸门环形缓冲。</summary>
        /// <param name="commit">整行提交回调（进入闸门环形缓冲）。</param>
        public SuppressingWriter(Action<string> commit) {
            _commit = commit;
        }

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value) {
            if (value == '\r') {
                return;
            }

            if (value == '\n') {
                CommitPending();
                return;
            }

            _pending.Append(value);
        }

        public override void Write(string? value) => Append(value);

        public override void WriteLine(string? value) {
            Append(value);
            CommitPending();
        }

        public override void WriteLine() => CommitPending();

        public override void Flush() { }

        protected override void Dispose(bool disposing) {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) {
                CommitPending();
            }
        }

        /// <summary>追加文本 — 内嵌 \n 切分逐行提交（\r 剥离），尾段留在缓冲等待后续换行。</summary>
        private void Append(string? value) {
            if (string.IsNullOrEmpty(value)) {
                return;
            }

            var start = 0;
            for (var i = 0; i < value.Length; i++) {
                if (value[i] != '\n') {
                    continue;
                }

                _pending.Append(value, start, i - start);
                CommitPending();
                start = i + 1;
            }

            if (start < value.Length) {
                var tail = value[start..];
                _pending.Append(tail.EndsWith('\r') ? tail[..^1] : tail);
            }
        }

        private void CommitPending() {
            if (_pending.Length == 0) {
                return;
            }

            var line = _pending.ToString().TrimEnd('\r');
            _pending.Clear();
            if (line.Length > 0) {
                _commit(line);
            }
        }
    }
}
