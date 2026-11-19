namespace JoinCode.App;

/// <summary>
/// 独立错误渲染器 — 不依赖任何 TUI 框架，仅使用 System.Console 输出
/// 用于感知程序报错，在 TUI 未启动或崩溃时仍可工作
/// </summary>
public static class ErrorConsole {
    private static readonly RenderMailbox Mailbox = new();
    private static int _isQuiet;

    /// <summary>
    /// 静默模式标志 — --quiet / -q 设置，抑制 Warning 和 Info 输出（Fatal/ApiError/StructuredError 仍显示）
    /// </summary>
    public static bool IsQuiet {
        get => Volatile.Read(ref _isQuiet) != 0;
        set => Volatile.Write(ref _isQuiet, value ? 1 : 0);
    }

    /// <summary>
    /// 渲染致命错误（红色标题 + 消息 + 堆栈）
    /// <para><b>控制台不打印堆栈</b>：终端是给用户看的，<c>at JoinCode.xxx</c> 对他没有任何信息量，
    /// 只会让人以为程序崩了。默认只留「类型: 消息」，堆栈在 <c>JCC_DEBUGLOG=1</c> 时才补；
    /// 完整堆栈另有 <c>~/.jcc/runtime/jcc_error.log</c> 与崩溃快照两处落点。</para>
    /// </summary>
    public static void Fatal(string message, Exception? ex = null) {
        Mailbox.Render(() => {
            Err();
            Colored("  ✖ 致命错误", System.ConsoleColor.Red);
            Err();
            Err($"  {message}");

            if (ex is not null) {
                Err();
                Colored("  详细信息:", System.ConsoleColor.DarkGray);
                Err($"  {ex.GetType().Name}: {ex.Message}");

                if (JoinCode.Abstractions.Utils.Diagnostics.Diag.IsDebugLog && !string.IsNullOrEmpty(ex.StackTrace)) {
                    Err();
                    foreach (var line in ex.StackTrace.Split('\n'))
                        Err($"  {line.Trim()}");
                }
            }

            Err();
        });
    }

    /// <summary>渲染警告（黄色标题 + 消息）— 静默模式下抑制</summary>
    public static void Warning(string message) {
        if (IsQuiet) return;
        Mailbox.Render(() => {
            Colored("  ⚠ 警告: ", System.ConsoleColor.Yellow);
            Err(message);
        });
    }

    /// <summary>渲染 API 错误（带错误分类）</summary>
    public static void ApiError(string message, string? suggestion = null) {
        Mailbox.Render(() => {
            Err();
            Colored("  ✖ API 错误", System.ConsoleColor.Red);
            Err($"  {message}");

            if (!string.IsNullOrEmpty(suggestion)) {
                Err();
                Colored("  建议: ", System.ConsoleColor.Cyan);
                Err(suggestion);
            }

            Err();
        });
    }

    /// <summary>渲染信息提示（灰色）— 静默模式下抑制</summary>
    public static void Info(string message) {
        if (IsQuiet) return;
        Mailbox.Render(() => {
            Colored("  ℹ ", System.ConsoleColor.DarkGray);
            Err(message);
        });
    }

    /// <summary>
    /// 渲染结构化错误 — 对齐架构指南4字段规范(code/message/hint/retryable)
    /// </summary>
    public static void StructuredError(Cli.Output.CliStructuredError error) {
        Mailbox.Render(() => {
            Err();
            Colored($"  ✖ [{error.Code}]", System.ConsoleColor.Red);
            Err($"  {error.Message}");

            if (!string.IsNullOrEmpty(error.Hint)) {
                Err();
                Colored("  💡 ", System.ConsoleColor.Cyan);
                Err(error.Hint);
            }

            if (error.Retryable) {
                Err();
                Colored("  ↻ ", System.ConsoleColor.DarkGray);
                Err("此错误可重试");
            }

            Err();
        });
    }

    /// <summary>单写者渲染邮箱 — 多行错误块整体入队，由唯一消费任务串行写终端。</summary>
    private sealed class RenderMailbox {
        // style-allow: unbounded — 同步 Render 阻塞等待消费完成,有界队列会与唯一写者互相死锁
        private readonly Channel<RenderCommand> _channel = Channel.CreateUnbounded<RenderCommand>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = false,
        });

        public RenderMailbox() {
            _ = FireAndForgetTask<Unit>.Run(ProcessAsync, "ErrorConsole.Render");
        }

        /// <summary>把整块渲染动作投递到唯一写者并等待其完成。</summary>
        public void Render(Action render) {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _channel.Writer.TryWrite(new RenderCommand(render, completion));
            // 同步 API 等待本行写完：消费端为独立线程池任务，无 SynchronizationContext
            completion.Task.GetAwaiter().GetResult();  // style-allow: sync-block — 崩溃诊断同步路径: 异常处理流程内不可 await
        }

        private async Task ProcessAsync(CancellationToken ct) {
            await foreach (var command in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false)) {
                try {
                    command.Render();
                } catch (Exception ex) {
                    // 消费任务必须存活：渲染异常只影响本条，否则后续 Render 将永久等待
                    System.Diagnostics.Debug.WriteLine($"[ErrorConsole] 终端渲染失败: {ex.Message}");
                } finally {
                    command.Completion.TrySetResult();
                }
            }
        }
    }

    private sealed record RenderCommand(Action Render, TaskCompletionSource Completion);

    private static void Err(string? text = null) {
        if (text is null) TerminalHelper.WriteError();
        else TerminalHelper.WriteError(text);
    }

    private static void Colored(string text, System.ConsoleColor color) {
        using var _ = TerminalHelper.SetColorRaw(color);
        TerminalHelper.WriteErrorRaw(text);
    }
}