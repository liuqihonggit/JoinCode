namespace JoinCode.Entry;

/// <summary>
/// 启动时询问用户是否打开调试信息 — 放在 WorkspaceTrustStep 之后
/// 决策: 交互模式才询问，非交互模式(-p)/JSON模式/测试环境跳过（不影响正常用户和脚本）
/// 决策: --debuglog 已启用时跳过询问，直接设为 All（用户已明确意图）
/// 决策: 用位标志枚举 DebugDumpSection，支持用户选择组合（如 i+p = Init|Prompt）
/// 交互解析支持: 字母组合(ip)、单词(init prompt)、数字(17)、分隔符(i,p / i+p / i p)
/// </summary>
[Register(typeof(IMiddleware<StartupContext>), ServiceLifetime.Singleton)]
internal sealed partial class DebugDumpPromptStep : ServiceEntity, IMiddleware<StartupContext>
{
    public async Task InvokeAsync(StartupContext context, MiddlewareDelegate<StartupContext> next, CancellationToken ct)
    {
        var options = context.Options;

        // --debuglog 已启用 → 直接设为 All，跳过询问（用户已明确意图）
        if (options.DebugLog && !options.IsJsonMode)
        {
            context.DebugDumpChoice = DebugDumpSection.All;
            Diag.WriteLine("[STEP] DebugDumpPrompt: --debuglog 已启用，跳过询问，设为 All");
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        // 非交互模式 / JSON 模式 / 测试环境 → 跳过询问
        if (options.IsNonInteractiveMode || options.IsJsonMode || Core.Utils.TestEnvironmentDetector.IsNonInteractive)
        {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        // 交互询问
        context.DebugDumpChoice = PromptDebugDumpChoice();

        if (context.DebugDumpChoice != DebugDumpSection.None)
        {
            Diag.WriteLine($"[STEP] DebugDumpPrompt: 用户选择 = {context.DebugDumpChoice} ({(int)context.DebugDumpChoice})");
        }

        await next(context, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 交互式询问用户要显示的调试信息类别
    /// </summary>
    private static DebugDumpSection PromptDebugDumpChoice()
    {
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine($"{TerminalColors.Accent}是否打开调试信息?{AnsiStyleConstants.Reset}");
        TerminalHelper.WriteLine($"  {TerminalColors.Muted}i(1)=初始化  e(2)=错误  w(4)=警告  l(8)=日志  p(16)=提示词{AnsiStyleConstants.Reset}");
        TerminalHelper.WriteLine($"  {TerminalColors.Muted}a(31)=全部  0=跳过{AnsiStyleConstants.Reset}");
        TerminalHelper.WriteRaw($"> {TerminalColors.Primary}");

        var input = TerminalHelper.ReadLine();
        TerminalHelper.WriteRaw(AnsiStyleConstants.Reset);

        return ParseDebugDumpInput(input);
    }

    /// <summary>
    /// 解析用户输入为 DebugDumpSection 位标志
    /// 支持格式: 数字(17)、字母组合(ip)、单词(init prompt)、分隔符(i,p / i+p / i p)
    /// </summary>
    internal static DebugDumpSection ParseDebugDumpInput(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return DebugDumpSection.None;

        input = input.Trim().ToLowerInvariant();

        // 数字 → 直接转换为枚举值
        if (int.TryParse(input, out var num))
        {
            if (num < 0 || num > (int)DebugDumpSection.All)
                return DebugDumpSection.None;
            return (DebugDumpSection)num;
        }

        // 单词/字母 → 用 FromValue 匹配（匹配到任何值包括 None 都直接返回，避免 "none" 被逐字符误匹配）
        var single = DebugDumpSectionExtensions.FromValue(input);
        if (single is { } section)
            return section;

        // 分隔符拆分 → 逐个 FromValue，位或组合
        var choice = DebugDumpSection.None;
        var parts = input.Split(' ', ',', '+', '|', ';');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            if (DebugDumpSectionExtensions.FromValue(trimmed) is { } flag && flag != DebugDumpSection.None)
                choice |= flag;
        }

        if (choice != DebugDumpSection.None)
            return choice;

        // 连续字母逐字符匹配（如 "ip" → Init | Prompt）
        var charChoice = DebugDumpSection.None;
        foreach (var c in input)
        {
            var flag = c switch
            {
                'i' => DebugDumpSection.Init,
                'e' => DebugDumpSection.Error,
                'w' => DebugDumpSection.Warn,
                'l' => DebugDumpSection.Log,
                'p' => DebugDumpSection.Prompt,
                'a' => DebugDumpSection.All,
                _ => DebugDumpSection.None,
            };
            charChoice |= flag;
        }

        return charChoice & DebugDumpSection.All;
    }
}
