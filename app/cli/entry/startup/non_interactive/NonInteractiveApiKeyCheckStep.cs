namespace JoinCode.Entry;

[Register(typeof(IMiddleware<StartupContext>), ServiceLifetime.Singleton)]
internal sealed partial class NonInteractiveApiKeyCheckStep : ServiceEntity, IMiddleware<StartupContext> {
    /// <summary>执行非交互模式 API Key 检查中间件 — 未配置 API Key 时输出错误并短路终止管道，已配置则传递给下一个中间件</summary>
    /// <param name="context">启动上下文，包含供应商配置</param>
    /// <param name="next">下一个中间件委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(StartupContext context, MiddlewareDelegate<StartupContext> next, CancellationToken ct) {
        Diag.WriteLine("[STEP] ApiKeyCheck start");
        if (string.IsNullOrEmpty(context.Config.Provider.ApiKey)) {
            // R-P2-002 修复: 非交互模式下无 API Key 时直接退出,避免后续 LLM 调用必然失败造成 401
            // 视角2 #24: 改用 ErrorConsole 渲染 + 添加配置方法提示
            App.ErrorConsole.ApiError("未配置 API Key，LLM 调用将失败");
            Cli.TerminalHelper.WriteError("  配置方法:");
            Cli.TerminalHelper.WriteError("    1. 环境变量: set DEEPSEEK_API_KEY=sk-xxx (按供应商设置)");
            Cli.TerminalHelper.WriteError("    2. 配置文件: .env/api.json");
            Cli.TerminalHelper.WriteError("    3. 交互模式运行 /init 命令");
            Cli.TerminalHelper.WriteError("  支持的环境变量: OPENAI_API_KEY / DEEPSEEK_API_KEY / ANTHROPIC_API_KEY / AZURE_OPENAI_API_KEY");
            context.ExitCode = (int)ExitCode.ApiKeyMissing;
            Diag.WriteLine("[STEP] ApiKeyCheck failed: missing API key, aborting pipeline");
            return;
        }
        Diag.WriteLine("[STEP] ApiKeyCheck done, calling next");
        await next(context, ct).ConfigureAwait(false);
    }
}