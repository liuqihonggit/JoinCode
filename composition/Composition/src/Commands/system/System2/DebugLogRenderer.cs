namespace JoinCode.ChatCommands;

/// <summary>
/// 诊断日志渲染器 — 提取自 DebugLogCommand，供启动流程（InitDebugDumpStep）和 /debuglog REPL 命令共用
/// 决策: 静态类 + 接收 IServiceProvider，避免依赖 ChatCommandContext（启动时无该上下文）
/// 替代方案已否决: 实例化服务 + DI 注入（过度设计，渲染逻辑无状态）
/// </summary>
public static class DebugLogRenderer
{
    /// <summary>
    /// 渲染全部诊断信息（等价 /debuglog -a）— 初始化状态 + 警告/错误 + 诊断日志 + 系统提示词
    /// </summary>
    /// <param name="services">DI 服务容器</param>
    /// <param name="cancellationToken">取消令牌（用于 IToolRegistry.GetCountAsync 等异步查询）</param>
    /// <returns>渲染后的完整文本（含 ANSI 颜色码）</returns>
    public static async Task<string> RenderAllAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        await AppendInitInfo(sb, services, cancellationToken).ConfigureAwait(false);
        AppendWarningsAndErrors(sb, services);
        AppendLogs(sb, services);
        await AppendSystemPrompt(sb, services).ConfigureAwait(false);
        return sb.ToString();
    }

    /// <summary>
    /// 渲染初始化信息（等价 /debuglog -i）— 调试日志状态 + 环境变量 + 崩溃快照 + 日志缓冲区 + MCP 工具 + 系统提示词部分清单
    /// </summary>
    public static async Task<string> RenderInitAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        await AppendInitInfo(sb, services, cancellationToken).ConfigureAwait(false);
        return sb.ToString();
    }

    /// <summary>
    /// 渲染错误（等价 /debuglog -e）— CrashSnapshotStore 中的 Error/Fatal + 诊断错误日志
    /// </summary>
    public static string RenderErrors(IServiceProvider services)
    {
        var sb = new StringBuilder();
        AppendErrors(sb, services);
        return sb.ToString();
    }

    /// <summary>
    /// 渲染警告和错误（等价 /debuglog -w）— CrashSnapshotStore 中的 Warning + Error/Fatal + 诊断错误日志
    /// </summary>
    public static string RenderWarningsAndErrors(IServiceProvider services)
    {
        var sb = new StringBuilder();
        AppendWarningsAndErrors(sb, services);
        return sb.ToString();
    }

    /// <summary>
    /// 渲染诊断日志（等价 /debuglog -l）— DebugLogBuffer 中的最近日志条目
    /// </summary>
    public static string RenderLogs(IServiceProvider services)
    {
        var sb = new StringBuilder();
        AppendLogs(sb, services);
        return sb.ToString();
    }

    /// <summary>
    /// 渲染系统提示词（等价 /debuglog -p）— ISystemPromptProvider.GetSections() 的所有部分内容
    /// </summary>
    public static async Task<string> RenderSystemPromptAsync(IServiceProvider services)
    {
        var sb = new StringBuilder();
        await AppendSystemPrompt(sb, services).ConfigureAwait(false);
        return sb.ToString();
    }

    /// <summary>
    /// 追加初始化信息到 StringBuilder
    /// </summary>
    private static async Task AppendInitInfo(StringBuilder sb, IServiceProvider services, CancellationToken cancellationToken)
    {
        sb.AppendLine($"{TerminalColors.Accent}═══ 初始化状态 ═══{AnsiStyleConstants.Reset}");

        var debugLogStatus = Diag.IsDebugLog
            ? $"{TerminalColors.Success}已启用{AnsiStyleConstants.Reset}"
            : $"{TerminalColors.Muted}未启用{AnsiStyleConstants.Reset}";
        sb.AppendLine($"  调试日志: {debugLogStatus}");

        var envVars = new[]
        {
            JccEnvVar.DebugLog, JccEnvVar.DiTrace, JccEnvVar.DumpMessages, JccEnvVar.LogLevel,
        };

        sb.AppendLine("  环境变量:");
        foreach (var envVar in envVars)
        {
            var value = Environment.GetEnvironmentVariable(envVar.ToValue());
            var display = value is not null
                ? $"{TerminalColors.Warning}{envVar.ToValue()}={value}{AnsiStyleConstants.Reset}"
                : $"{TerminalColors.Muted}{envVar.ToValue()}=(未设置){AnsiStyleConstants.Reset}";
            sb.AppendLine($"    {display}");
        }

        var crashStore = services.GetService<ICrashSnapshotStore>();
        if (crashStore is not null)
        {
            sb.AppendLine($"  崩溃快照: {TerminalColors.Muted}{crashStore.TotalCount} 条记录, {crashStore.UnacknowledgedCount} 条未确认{AnsiStyleConstants.Reset}");
        }

        var debugBuffer = services.GetService<IDebugLogBuffer>();
        if (debugBuffer is not null)
        {
            sb.AppendLine($"  日志缓冲区: {TerminalColors.Muted}{debugBuffer.Count} 条{AnsiStyleConstants.Reset}");
        }

        var toolRegistry = services.GetService<IToolRegistry>();
        if (toolRegistry is not null)
        {
            var toolCount = await toolRegistry.GetCountAsync(cancellationToken).ConfigureAwait(false);
            sb.AppendLine($"  MCP 工具: {TerminalColors.Muted}{toolCount} 个已注册{AnsiStyleConstants.Reset}");
        }

        var promptProvider = services.GetService<ISystemPromptProvider>();
        if (promptProvider is not null)
        {
            var sections = promptProvider.GetSections().ToList();
            sb.AppendLine($"  系统提示词: {TerminalColors.Muted}{sections.Count} 个部分{AnsiStyleConstants.Reset}");
            foreach (var section in sections)
            {
                var cacheTag = section.CacheBreak ? "动态" : "缓存";
                sb.AppendLine($"    {TerminalColors.Muted}[{cacheTag}]{AnsiStyleConstants.Reset} {section.Name}");
            }
        }

        sb.AppendLine();
    }

    /// <summary>
    /// 追加警告和错误到 StringBuilder
    /// </summary>
    private static void AppendWarningsAndErrors(StringBuilder sb, IServiceProvider services)
    {
        var crashStore = services.GetService<ICrashSnapshotStore>();
        var debugBuffer = services.GetService<IDebugLogBuffer>();

        sb.AppendLine($"{TerminalColors.Accent}═══ 警告与错误 ═══{AnsiStyleConstants.Reset}");

        if (crashStore is not null)
        {
            var warnings = crashStore.GetRecent(50).Where(s => s.Severity == CrashSeverity.Warning).ToList();
            var errors = crashStore.GetRecent(50).Where(s => s.Severity is CrashSeverity.Error or CrashSeverity.Fatal).ToList();

            if (warnings.Count > 0)
            {
                sb.AppendLine($"  {TerminalColors.Warning}警告 ({warnings.Count}):{AnsiStyleConstants.Reset}");
                foreach (var w in warnings.Take(20))
                {
                    sb.AppendLine($"    [{w.Severity.ToValue()}] {w.FenceName}: {w.ExceptionType}: {w.ExceptionMessage}");
                }
            }

            if (errors.Count > 0)
            {
                sb.AppendLine($"  {TerminalColors.Error}错误 ({errors.Count}):{AnsiStyleConstants.Reset}");
                foreach (var e in errors.Take(20))
                {
                    sb.AppendLine($"    [{e.Severity.ToValue()}] {e.FenceName}: {e.ExceptionType}: {e.ExceptionMessage}");
                    if (e.ExecutionContext.ToolName is not null)
                        sb.AppendLine($"      工具: {e.ExecutionContext.ToolName}  轮次: {e.ExecutionContext.TurnIndex}");
                    sb.AppendLine($"      时间: {e.CapturedAt:HH:mm:ss.fff}  ID: {e.Id:N}");
                }
            }

            if (warnings.Count == 0 && errors.Count == 0)
            {
                sb.AppendLine($"  {TerminalColors.Success}无警告或错误{AnsiStyleConstants.Reset}");
            }
        }
        else
        {
            sb.AppendLine($"  {TerminalColors.Muted}CrashSnapshotStore 不可用{AnsiStyleConstants.Reset}");
        }

        if (debugBuffer is not null)
        {
            var errorLogs = debugBuffer.GetByLevel(DebugLogLevel.Error, 30);
            if (errorLogs.Count > 0)
            {
                sb.AppendLine($"  {TerminalColors.Error}诊断错误日志 ({errorLogs.Count}):{AnsiStyleConstants.Reset}");
                foreach (var entry in errorLogs)
                {
                    sb.AppendLine($"    {entry.Timestamp:HH:mm:ss.fff} {entry.Message}");
                }
            }
        }

        sb.AppendLine();
    }

    /// <summary>
    /// 追加错误到 StringBuilder
    /// </summary>
    private static void AppendErrors(StringBuilder sb, IServiceProvider services)
    {
        var crashStore = services.GetService<ICrashSnapshotStore>();
        var debugBuffer = services.GetService<IDebugLogBuffer>();

        sb.AppendLine($"{TerminalColors.Error}═══ 错误 ═══{AnsiStyleConstants.Reset}");

        var hasErrors = false;

        if (crashStore is not null)
        {
            var errors = crashStore.GetRecent(50).Where(s => s.Severity is CrashSeverity.Error or CrashSeverity.Fatal).ToList();
            if (errors.Count > 0)
            {
                hasErrors = true;
                foreach (var e in errors)
                {
                    sb.AppendLine($"  [{e.Severity.ToValue()}] {e.FenceName}");
                    sb.AppendLine($"    {e.ExceptionType}: {e.ExceptionMessage}");
                    if (e.ErrorCode is not null)
                        sb.AppendLine($"    错误码: {e.ErrorCode}");
                    if (e.ExecutionContext.ToolName is not null)
                        sb.AppendLine($"    工具: {e.ExecutionContext.ToolName}  轮次: {e.ExecutionContext.TurnIndex}");
                    sb.AppendLine($"    时间: {e.CapturedAt:HH:mm:ss.fff}  ID: {e.Id:N}");
                    sb.AppendLine();
                }
            }
        }

        if (debugBuffer is not null)
        {
            var errorLogs = debugBuffer.GetByLevel(DebugLogLevel.Error, 50);
            if (errorLogs.Count > 0)
            {
                hasErrors = true;
                sb.AppendLine("  诊断错误日志:");
                foreach (var entry in errorLogs)
                {
                    sb.AppendLine($"    {entry.Timestamp:HH:mm:ss.fff} {entry.Message}");
                }
            }
        }

        if (!hasErrors)
        {
            sb.AppendLine($"  {TerminalColors.Success}无错误记录{AnsiStyleConstants.Reset}");
        }

        sb.AppendLine();
    }

    /// <summary>
    /// 追加诊断日志到 StringBuilder
    /// </summary>
    private static void AppendLogs(StringBuilder sb, IServiceProvider services)
    {
        var debugBuffer = services.GetService<IDebugLogBuffer>();

        sb.AppendLine($"{TerminalColors.Accent}═══ 诊断日志 ═══{AnsiStyleConstants.Reset}");

        if (debugBuffer is null)
        {
            sb.AppendLine($"  {TerminalColors.Muted}DebugLogBuffer 不可用{AnsiStyleConstants.Reset}");
            sb.AppendLine();
            return;
        }

        var entries = debugBuffer.GetRecent(100);
        if (entries.Count == 0)
        {
            sb.AppendLine($"  {TerminalColors.Muted}无日志记录（启用 --debuglog 可捕获更多日志）{AnsiStyleConstants.Reset}");
            sb.AppendLine();
            return;
        }

        var grouped = entries.GroupBy(e => e.Category).OrderByDescending(g => g.Count()).ToList();
        sb.AppendLine($"  共 {debugBuffer.Count} 条日志，显示最近 {entries.Count} 条");
        sb.AppendLine($"  分类统计: {string.Join(", ", grouped.Select(g => $"{g.Key}={g.Count()}"))}");
        sb.AppendLine();

        foreach (var entry in entries.Take(80))
        {
            var levelColor = entry.Level switch
            {
                DebugLogLevel.Error => TerminalColors.Error,
                DebugLogLevel.Warn => TerminalColors.Warning,
                _ => TerminalColors.Muted,
            };
            sb.AppendLine($"  {levelColor}{entry.Timestamp:HH:mm:ss.fff} [{entry.Category}]{AnsiStyleConstants.Reset} {Truncate(entry.Message, 200)}");
        }

        if (entries.Count > 80)
            sb.AppendLine($"  ... 还有 {entries.Count - 80} 条");

        sb.AppendLine();
    }

    /// <summary>
    /// 追加系统提示词到 StringBuilder — 显示每个 section 的内容（截断到 500 字符）
    /// </summary>
    private static async Task AppendSystemPrompt(StringBuilder sb, IServiceProvider services)
    {
        var promptProvider = services.GetService<ISystemPromptProvider>();

        sb.AppendLine($"{TerminalColors.Accent}═══ 系统提示词 ═══{AnsiStyleConstants.Reset}");

        if (promptProvider is null)
        {
            sb.AppendLine($"  {TerminalColors.Muted}ISystemPromptProvider 不可用{AnsiStyleConstants.Reset}");
            sb.AppendLine();
            return;
        }

        var sections = promptProvider.GetSections().ToList();
        if (sections.Count == 0)
        {
            sb.AppendLine($"  {TerminalColors.Muted}无系统提示词部分{AnsiStyleConstants.Reset}");
            sb.AppendLine();
            return;
        }

        var totalLength = 0;
        foreach (var section in sections)
        {
            var cacheTag = section.CacheBreak ? "动态" : "缓存";
            sb.AppendLine($"  {TerminalColors.Primary}[{cacheTag}] {section.Name}{AnsiStyleConstants.Reset}");

            try
            {
                var content = await section.ComputeValueTaskAsync().ConfigureAwait(false);
                if (content is not null)
                {
                    totalLength += content.Length;
                    var display = content.Length > 500
                        ? content[..500] + $"... (共 {content.Length} 字符)"
                        : content;
                    sb.AppendLine($"    {TerminalColors.Muted}{display}{AnsiStyleConstants.Reset}");
                }
                else
                {
                    sb.AppendLine($"    {TerminalColors.Muted}(空){AnsiStyleConstants.Reset}");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"    {TerminalColors.Error}计算失败: {ex.Message}{AnsiStyleConstants.Reset}");
            }

            sb.AppendLine();
        }

        sb.AppendLine($"  总计: {sections.Count} 个部分, {totalLength:N0} 字符");
        sb.AppendLine();
    }

    /// <summary>
    /// 截断字符串到指定长度，超出部分用 "..." 表示
    /// </summary>
    private static string Truncate(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}
