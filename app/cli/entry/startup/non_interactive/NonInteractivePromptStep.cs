namespace JoinCode.Entry;

[Register(typeof(IMiddleware<StartupContext>), ServiceLifetime.Singleton)]
internal sealed partial class NonInteractivePromptStep : ServiceEntity, IMiddleware<StartupContext> {
    /// <summary>执行非交互模式提示词读取中间件 — 从 CLI 选项或 stdin 读取提示词并写入上下文，空提示词时短路终止管道</summary>
    /// <param name="context">启动上下文，用于存储读取到的提示词</param>
    /// <param name="next">下一个中间件委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(StartupContext context, MiddlewareDelegate<StartupContext> next, CancellationToken ct) {
        Diag.WriteLine("[STEP] PromptStep start");
        var prompt = context.Options.Prompt;
        Diag.WriteLine($"[STEP] PromptStep options.Prompt={(string.IsNullOrWhiteSpace(prompt) ? "<null/empty>" : $"'{prompt}'")}");
        if (string.IsNullOrWhiteSpace(prompt)) {
            Diag.WriteLine("[STEP] PromptStep reading from stdin...");
            prompt = await System.Console.In.ReadToEndAsync(ct).ConfigureAwait(false);
            Diag.WriteLine($"[STEP] PromptStep stdin read, length={prompt?.Length ?? 0}");
        }

        if (string.IsNullOrWhiteSpace(prompt)) {
            Cli.TerminalHelper.WriteLine("错误: 未提供提示词。");
            context.ExitCode = (int)ExitCode.ArgumentParseError;
            Diag.WriteLine("[STEP] PromptStep empty, short-circuit");
            return;
        }

        context.NonInteractivePrompt = prompt;
        Diag.WriteLine("[STEP] PromptStep done, calling next");
        await next(context, ct).ConfigureAwait(false);
    }
}