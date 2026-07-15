namespace JoinCode.Entry;

/// <summary>
/// 系统提示词应用中间件 — 处理 --system-prompt 和 --append-system-prompt CLI 参数
/// 在 SessionResumeStep 之后执行，确保会话恢复后再应用提示词覆盖
/// 对齐 TS: claude --system-prompt / claude --append-system-prompt
/// </summary>
[Register]
internal sealed partial class SystemPromptApplyStep : IMiddleware<StartupContext>
{
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async Task InvokeAsync(StartupContext context, MiddlewareDelegate<StartupContext> next, CancellationToken ct)
    {
        var options = context.Options;

        // 无 --system-prompt 也无 --append-system-prompt → 跳过
        if (string.IsNullOrEmpty(options.SystemPrompt) && string.IsNullOrEmpty(options.AppendSystemPrompt))
        {
            await next(context, ct);
            return;
        }

        var sp = context.Host.Services;

        // --system-prompt: 完全覆盖默认系统提示词
        // 决策: 使用 IChatService.SetSystemPromptAsync（通过 admin 管道执行，会触发 UpdateSystemPromptAsync 替换 _staticSystemPrompt）
        // 替代方案已否决: 直接调用 IChatContextManager（绕过 admin 管道，丢失审计）
        if (!string.IsNullOrEmpty(options.SystemPrompt))
        {
            var chatService = sp.GetRequiredService<IChatService>();
            await chatService.SetSystemPromptAsync(options.SystemPrompt, ct).ConfigureAwait(false);
            Diag.WriteLine($"[STEP] SystemPromptApply: --system-prompt 已应用，长度={options.SystemPrompt.Length}");
        }

        // --append-system-prompt: 在默认/已加载系统提示词后追加，不覆盖
        // 决策: 使用 IChatContextManager.AddDynamicSystemMessageAsync（添加到 _dynamicSystemMessages 列表，组装时拼接在 _staticSystemPrompt 之后）
        // 替代方案已否决: 读取当前 _staticSystemPrompt 再 SetSystemPromptAsync（破坏封装，且 _staticSystemPrompt 无公开 getter）
        // 与 --system-prompt 同时指定时的语义: 先覆盖静态，再追加动态 — 最终前缀 = newStatic + dynamicAppend
        if (!string.IsNullOrEmpty(options.AppendSystemPrompt))
        {
            var contextManager = sp.GetRequiredService<IChatContextManager>();
            await contextManager.AddDynamicSystemMessageAsync(options.AppendSystemPrompt, ct).ConfigureAwait(false);
            Diag.WriteLine($"[STEP] SystemPromptApply: --append-system-prompt 已应用，长度={options.AppendSystemPrompt.Length}");
        }

        await next(context, ct);
    }
}
