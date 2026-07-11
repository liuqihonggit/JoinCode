namespace JoinCode.Entry;

[Register]
internal sealed class NonInteractiveApiKeyCheckStep : IMiddleware<StartupContext>
{
    public async Task InvokeAsync(StartupContext context, MiddlewareDelegate<StartupContext> next, CancellationToken ct)
    {
        Console.Error.WriteLine("[STEP] ApiKeyCheck start");
        if (string.IsNullOrEmpty(context.Config.Provider.ApiKey))
        {
            Cli.TerminalHelper.WriteLine("警告: 未配置 API Key，LLM 调用将失败。请通过环境变量设置 (JCC_API_KEY / OPENAI_API_KEY / ANTHROPIC_API_KEY)。");
        }
        Console.Error.WriteLine("[STEP] ApiKeyCheck done, calling next");
        await next(context, ct);
    }
}
