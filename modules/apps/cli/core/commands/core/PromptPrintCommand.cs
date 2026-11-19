namespace JoinCode.CliCommands;

/// <summary>
/// prompt 自省命令 — 打印系统提示词各段的真实内容。
/// <para>存在理由：提示词是模型行为的第一因，但此前没有任何非交互手段能看到它。
/// 改 <c>ToolsSection</c> 这类段落后，只能靠「跑一轮看模型回什么」猜，或者依赖单测断言字符串 ——
/// 而单测验的是 <c>GetContent()</c>，不是「这段真的进了发出去的提示词」。</para>
/// <para>本命令走 <see cref="McpCliCommand.WithHostAsync"/> 构建完整宿主，因此拿到的是
/// 与聊天链路同一份 <see cref="ISystemPromptProvider"/>，包括段落清单与真实装配结果。</para>
/// </summary>
internal static class PromptPrintCommand {
    private const string SectionOption = "--section";
    private const string ListOption = "--list";

    /// <summary>
    /// 执行 prompt — 无参数时列出全部段落及字符数；<c>--section &lt;名字&gt;</c> 打印该段全文；
    /// <c>--section all</c> 打印全部（体积可达数十 KB，只建议重定向到文件后检索）。
    /// </summary>
    public static async Task<int> ExecuteAsync(string[] args, CancellationToken ct) {
        var requested = FlatSubCommandRouter.GetOptionValue(args, SectionOption);
        var listOnly = args.Any(arg => string.Equals(arg, ListOption, StringComparison.Ordinal));

        return await McpCliCommand.WithHostAsync(async services => {
            var provider = services.GetService<ISystemPromptProvider>();
            if (provider is null) {
                TerminalHelper.WriteError("ISystemPromptProvider 未注册（prompt 命令的宿主装配层拦截）：宿主没有加载提示词服务。");
                TerminalHelper.WriteError("修复方式：确认本命令经 jcc 完整宿主启动（不要用 SkipChatServices 的轻量宿主）。");
                return 1;
            }

            // 工具组清单随 profile 变（tool-groups 消费点），生产链路由 SystemPromptMiddleware 开作用域；
            // 本命令走 provider 路径，必须自己开，否则「打印出来的提示词」比真实发送的多列几组 ——
            // 自省命令一旦与生产不一致，就不再是自省。
            var profileManager = services.GetService<Abstractions.Composition.IProfileManager>();
            using var toolGroupScope = Core.Prompts.PromptToolGroupScope.Enter(
                profileManager is null
                    ? null
                    : Abstractions.Composition.Profile.ToolGroupDeclaration.EnabledIds(profileManager.Current));

            var sections = provider.GetSections().ToList();
            if (sections.Count == 0) {
                TerminalHelper.WriteError("系统提示词没有任何段落（prompt 命令的装配结果校验层拦截）：[PromptSection] 未被生成器收集。");
                return 1;
            }

            return string.IsNullOrWhiteSpace(requested) || listOnly
                ? await ListSectionsAsync(sections).ConfigureAwait(false)
                : await PrintSectionAsync(sections, requested).ConfigureAwait(false);
        }, ct: ct).ConfigureAwait(false);
    }

    /// <summary>列出段落名与字符数 —— 想知道「一共有哪些段、哪段忽然变长了」时用这个。</summary>
    private static async Task<int> ListSectionsAsync(IReadOnlyList<SystemPromptSection> sections) {
        Console.Out.WriteLine($"系统提示词共 {sections.Count} 段：");
        var total = 0;
        foreach (var section in sections) {
            var content = await section.ComputeValueTaskAsync().ConfigureAwait(false);
            total += content?.Length ?? 0;
            var cacheTag = section.CacheBreak ? "动态" : "缓存";
            Console.Out.WriteLine($"  {section.Name,-28} {cacheTag}  {(content?.Length ?? 0),7} 字符");
        }

        Console.Out.WriteLine($"合计 {total} 字符（缓存段可被供应商前缀缓存复用）");
        Console.Out.WriteLine($"用法: jcc prompt --section <名字> 打印该段全文；--section all 打印全部");
        return 0;
    }

    /// <summary>打印指定段（或 all）的全文 —— 提示词改动后的当场核对入口。</summary>
    private static async Task<int> PrintSectionAsync(IReadOnlyList<SystemPromptSection> sections, string requested) {
        if (string.Equals(requested, "all", StringComparison.OrdinalIgnoreCase)) {
            foreach (var section in sections) {
                var text = await section.ComputeValueTaskAsync().ConfigureAwait(false);
                Console.Out.WriteLine($"===== {section.Name} =====");
                Console.Out.WriteLine(text ?? "(空)");
                Console.Out.WriteLine();
            }
            return 0;
        }

        var target = sections.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, requested, StringComparison.OrdinalIgnoreCase));
        if (target is null) {
            TerminalHelper.WriteError($"未找到提示词段落 '{requested}'（prompt 命令的段落查找层拦截）。");
            TerminalHelper.WriteError($"现有段落: {string.Join(", ", sections.Select(section => section.Name))}");
            TerminalHelper.WriteError("正确写法: jcc prompt --section tools");
            return 1;
        }

        var content = await target.ComputeValueTaskAsync().ConfigureAwait(false);
        Console.Out.WriteLine(content ?? "(空)");
        return 0;
    }
}
