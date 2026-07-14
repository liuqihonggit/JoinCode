namespace JoinCode.Entry;

[Register]
internal sealed class NonInteractiveApiKeyCheckStep : IMiddleware<StartupContext>
{
    public async Task InvokeAsync(StartupContext context, MiddlewareDelegate<StartupContext> next, CancellationToken ct)
    {
        Diag.WriteLine("[STEP] ApiKeyCheck start");
        if (string.IsNullOrEmpty(context.Config.Provider.ApiKey))
        {
            // R-P2-002 修复: 非交互模式下无 API Key 时直接退出,避免后续 LLM 调用必然失败造成 401
            // 视角2 #24: 改用 ErrorConsole 渲染 + 添加配置方法提示
            App.ErrorConsole.ApiError("未配置 API Key，LLM 调用将失败");
            Cli.TerminalHelper.WriteError("  配置方法:");
            Cli.TerminalHelper.WriteError("    1. 环境变量: set JCC_API_KEY=sk-xxx");
            Cli.TerminalHelper.WriteError("    2. 配置文件: .env/api.json");
            Cli.TerminalHelper.WriteError("    3. 交互模式运行 /init 命令");
            Cli.TerminalHelper.WriteError("  支持的环境变量: JCC_API_KEY / OPENAI_API_KEY / ANTHROPIC_API_KEY / AZURE_OPENAI_API_KEY");
            context.ExitCode = 1;
            Diag.WriteLine("[STEP] ApiKeyCheck failed: missing API key, aborting pipeline");
            return;
        }
        Diag.WriteLine("[STEP] ApiKeyCheck done, calling next");
        await next(context, ct);
    }
}
