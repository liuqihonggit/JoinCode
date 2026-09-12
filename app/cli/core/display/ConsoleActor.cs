namespace JoinCode.Cli.Display;

/// <summary>
/// Console I/O Actor — 将所有 Console 读写操作封装为命令，通过 Channel FIFO 串行化执行。
/// <para>消除后台异步输出（如 ILogger→AddConsole→Console.Out）与主线程 Console.ReadLine 的竞态。</para>
/// <para>架构决策见 ADR 0100。复用 ActorBase&lt;TCommand, TOut&gt;（foundation/AsyncLock）。</para>
/// </summary>
public sealed class ConsoleActor : ActorBase<ConsoleCommand, Unit>
{
    private readonly TextWriter _realOut;

    /// <summary>
    /// 创建 ConsoleActor — realOut 为 Init 时捕获的原始 Console.Out，Actor 内部输出走此实例
    /// </summary>
    public ConsoleActor(TextWriter realOut) : base()
    {
        _realOut = realOut;
    }

    protected override ValueTask HandleAsync(ConsoleCommand command, CancellationToken ct)
    {
        switch (command)
        {
            case WriteLineCmd(var text):
                if (text is null) _realOut.WriteLine();
                else _realOut.WriteLine(text);
                _realOut.Flush();
                break;

            case WriteRawCmd(var text):
                _realOut.Write(text);
                _realOut.Flush();
                break;

            case ReadLineCmd(var reply):
#pragma warning disable JCC2001 // Actor 串行化执行，调用方已检查 IsInputRedirected
                var line = System.Console.ReadLine() ?? string.Empty;
#pragma warning restore JCC2001
                reply.TrySetResult(line);
                break;

            case ReadLineRawCmd(var reply):
#pragma warning disable JCC2001 // Actor 串行化执行，调用方已检查 IsInputRedirected
                var rawLine = System.Console.ReadLine();
#pragma warning restore JCC2001
                reply.TrySetResult(rawLine);
                break;

            case ReadKeyCmd(var intercept, var reply):
#pragma warning disable JCC2002 // Actor 串行化执行，调用方已检查 IsInputRedirected
                var key = System.Console.ReadKey(intercept);
#pragma warning restore JCC2002
                reply.TrySetResult(key);
                break;

            case ClearScreenCmd:
                if (!System.Console.IsOutputRedirected)
                    System.Console.Clear();
                break;

            case SetCursorPositionCmd(var left, var top):
                System.Console.SetCursorPosition(left, top);
                break;

            case SetColorCmd(var color, var reply):
                var prev = System.Console.ForegroundColor;
                System.Console.ForegroundColor = color;
                reply.TrySetResult(prev);
                break;

            case ResetColorCmd:
                System.Console.ResetColor();
                break;

            case FlushCmd:
                _realOut.Flush();
                break;
        }
        return ValueTask.CompletedTask;
    }

    /// <summary>写入一行（经过 Actor 串行化）</summary>
    public void WriteLine(string? text = null) => TrySend(new WriteLineCmd(text));

    /// <summary>写入原始文本（经过 Actor 串行化）</summary>
    public void WriteRaw(string text) => TrySend(new WriteRawCmd(text));

    /// <summary>
    /// 读取一行 — 经过 Actor 串行化，确保 ReadLine 期间无后台输出交错。
    /// <para>同步阻塞等待 Actor 处理完成（CLI 场景无 SynchronizationContext，不会死锁）。</para>
    /// </summary>
    public string ReadLine()
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        TrySend(new ReadLineCmd(tcs));
        return tcs.Task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// 读取一行（保留 null EOF 语义）— 经过 Actor 串行化。
    /// <para>返回 null 表示 EOF（管道关闭），与 Console.ReadLine() 语义一致。</para>
    /// </summary>
    public string? ReadLineOrNull()
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        TrySend(new ReadLineRawCmd(tcs));
        return tcs.Task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// 读取一个按键 — 经过 Actor 串行化。
    /// </summary>
    public ConsoleKeyInfo ReadKey(bool intercept = false)
    {
        var tcs = new TaskCompletionSource<ConsoleKeyInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        TrySend(new ReadKeyCmd(intercept, tcs));
        return tcs.Task.GetAwaiter().GetResult();
    }

    /// <summary>清屏（经过 Actor 串行化）</summary>
    public void ClearScreen() => TrySend(new ClearScreenCmd());

    /// <summary>设置光标位置（经过 Actor 串行化）</summary>
    public void SetCursorPosition(int left, int top) => TrySend(new SetCursorPositionCmd(left, top));

    /// <summary>
    /// 设置前景色 — 返回之前的颜色（经过 Actor 串行化）
    /// </summary>
    public ConsoleColor SetColor(ConsoleColor color)
    {
        var tcs = new TaskCompletionSource<ConsoleColor>(TaskCreationOptions.RunContinuationsAsynchronously);
        TrySend(new SetColorCmd(color, tcs));
        return tcs.Task.GetAwaiter().GetResult();
    }

    /// <summary>重置颜色（经过 Actor 串行化）</summary>
    public void ResetColor() => TrySend(new ResetColorCmd());

    /// <summary>刷新输出缓冲（经过 Actor 串行化）</summary>
    public void Flush() => TrySend(new FlushCmd());
}

/// <summary>Console I/O 命令基类 — 所有命令通过 Channel FIFO 串行执行</summary>
public abstract record ConsoleCommand;

/// <summary>写入一行</summary>
public sealed record WriteLineCmd(string? Text) : ConsoleCommand;

/// <summary>写入原始文本（不换行）</summary>
public sealed record WriteRawCmd(string Text) : ConsoleCommand;

/// <summary>读取一行 — Reply 在 Actor 处理时完成</summary>
public sealed record ReadLineCmd(TaskCompletionSource<string> Reply) : ConsoleCommand;

/// <summary>读取一行（保留 null EOF 语义）— Reply 在 Actor 处理时完成</summary>
public sealed record ReadLineRawCmd(TaskCompletionSource<string?> Reply) : ConsoleCommand;

/// <summary>读取一个按键 — Reply 在 Actor 处理时完成</summary>
public sealed record ReadKeyCmd(bool Intercept, TaskCompletionSource<ConsoleKeyInfo> Reply) : ConsoleCommand;

/// <summary>清屏</summary>
public sealed record ClearScreenCmd : ConsoleCommand;

/// <summary>设置光标位置</summary>
public sealed record SetCursorPositionCmd(int Left, int Top) : ConsoleCommand;

/// <summary>设置前景色 — Reply 返回之前的颜色</summary>
public sealed record SetColorCmd(ConsoleColor Color, TaskCompletionSource<ConsoleColor> Reply) : ConsoleCommand;

/// <summary>重置颜色</summary>
public sealed record ResetColorCmd : ConsoleCommand;

/// <summary>刷新输出缓冲</summary>
public sealed record FlushCmd : ConsoleCommand;
