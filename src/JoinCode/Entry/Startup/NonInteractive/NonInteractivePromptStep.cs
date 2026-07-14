namespace JoinCode.Entry;

[Register]
internal sealed class NonInteractivePromptStep : IMiddleware<StartupContext>
{
    public async Task InvokeAsync(StartupContext context, MiddlewareDelegate<StartupContext> next, CancellationToken ct)
    {
        Diag.WriteLine("[STEP] PromptStep start");
        var prompt = context.Options.Prompt;
        Diag.WriteLine($"[STEP] PromptStep options.Prompt={(string.IsNullOrWhiteSpace(prompt) ? "<null/empty>" : $"'{prompt}'")}");
        if (string.IsNullOrWhiteSpace(prompt))
        {
            Diag.WriteLine("[STEP] PromptStep reading from stdin...");
            prompt = await System.Console.In.ReadToEndAsync(ct);
            Diag.WriteLine($"[STEP] PromptStep stdin read, length={prompt?.Length ?? 0}");
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            Cli.TerminalHelper.WriteLine("错误: 未提供提示词。");
            context.ExitCode = 1;
            Diag.WriteLine("[STEP] PromptStep empty, short-circuit");
            return;
        }

        context.NonInteractivePrompt = prompt;
        Diag.WriteLine("[STEP] PromptStep done, calling next");
        await next(context, ct);
    }
}
