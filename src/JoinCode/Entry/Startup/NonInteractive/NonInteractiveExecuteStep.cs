namespace JoinCode.Entry;

[Register]
internal sealed class NonInteractiveExecuteStep : IMiddleware<StartupContext>
{
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async Task InvokeAsync(StartupContext context, MiddlewareDelegate<StartupContext> next, CancellationToken ct)
    {
        Console.Error.WriteLine("[STEP] ExecuteStep start");
        var session = context.Session;
        if (session is null)
        {
            Console.Error.WriteLine("[STEP] ExecuteStep ERROR: context.Session is null!");
            context.ExitCode = 1;
            return;
        }
        Console.Error.WriteLine($"[STEP] ExecuteStep session={session.GetType().Name}");

        try
        {
        Console.Error.WriteLine("[STEP] ExecuteStep calling ProcessUserInputAsync...");
        Console.Error.WriteLine("[READY]");
        var prompt = context.NonInteractivePrompt;
            if (string.IsNullOrEmpty(prompt))
            {
                Console.Error.WriteLine("[STEP] ExecuteStep ERROR: NonInteractivePrompt is null/empty!");
                context.ExitCode = 1;
                return;
            }
            await session.ProcessUserInputAsync(prompt, ct);
            await Console.Out.FlushAsync().ConfigureAwait(false);
            Console.Error.WriteLine("[DONE]");
            Console.Error.WriteLine("[STEP] ExecuteStep ProcessUserInputAsync returned");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[STEP] ExecuteStep exception: {ex.GetType().Name}: {ex.Message}");
            Cli.TerminalHelper.WriteLine($"错误: {ex.Message}");
            context.ExitCode = 1;
            return;
        }

        Console.Error.WriteLine("[STEP] ExecuteStep done, calling next");
        Console.Error.WriteLine("[EXIT]");
        await next(context, ct);
    }
}
