namespace JoinCode.Entry;

[Register]
internal sealed class NonInteractiveApiKeyCheckStep : IMiddleware<StartupContext>
{
    public async Task InvokeAsync(StartupContext context, MiddlewareDelegate<StartupContext> next, CancellationToken ct)
    {
        Console.Error.WriteLine("[STEP] ApiKeyCheck start");
        if (string.IsNullOrEmpty(context.Config.Provider.ApiKey))
        {
            // R-P2-002 修复: 非交互模式下无 API Key 时直接退出,避免后续 LLM 调用必然失败造成 401
            Cli.TerminalHelper.WriteLine("错误: 未配置 API Key,LLM 调用将失败。请通过环境变量设置 (JCC_API_KEY / OPENAI_API_KEY / ANTHROPIC_API_KEY)。");
            context.ExitCode = 1;
            Console.Error.WriteLine("[STEP] ApiKeyCheck failed: missing API key, aborting pipeline");
            return;
        }
        Console.Error.WriteLine("[STEP] ApiKeyCheck done, calling next");
        await next(context, ct);
    }
}
