namespace JoinCode.ChatCommands;

/// <summary>
/// 诚实模块 — 用户切换模型后，复述「已确认的目标」与「还缺什么」。
/// <para>行为严格对齐系统提示词 <c>goal_driven</c> 段最后一条：
/// 「用户切换目标或切换模型时，先复述已确认的目标和诚实清单里未完成项，等用户重新确认方向再继续」。
/// 因此这里打印两段而不是一段：先目标（从 <see cref="IGoalEngine"/> 读运行时真值，
/// 不是从对话里推测），再诚实清单里**未完成**的项。已经完成的项不复述 —— 它们不构成「可能遗漏」，
/// 全列出来只会把真正的缺口埋掉。</para>
/// <para>本模块只读清单与目标状态，不改任务状态、不触发任何后续动作 —— 方向必须由用户决定。</para>
/// <para>清单位于源码仓库而非内嵌资源：它是给人维护、随进度打勾的活文档，
/// 每次切换都读当前内容，避免清单改了却要重编 exe 才生效。</para>
/// </summary>
internal static class HonestyNotice {
    private sealed record Item(string Id, string Title, string Status, string Evidence, string Next);
    private sealed record Checklist(string Doc, IReadOnlyList<Item> Items);

    /// <summary>模型切换成功后调用 —— 先复述目标，再列未完成项，然后等用户决定方向。</summary>
    public static async Task PrintAfterModelSwitchAsync(ChatCommandContext context, string modelId) {
        var fs = ChatCommandBase.GetService<IFileSystem>(context, typeof(IFileSystem));
        var checklist = fs is null ? null : await LoadChecklistAsync(fs).ConfigureAwait(false);

        TerminalHelper.WriteLine($"{TerminalColors.Warning}═══ 诚实提示：模型已切换为 {modelId} ═══{AnsiStyleEnumConstants.Reset}");
        await PrintGoalAsync(context).ConfigureAwait(false);
        PrintChecklist(checklist);

        TerminalHelper.WriteLine("请确认方向：① 继续上述目标 ② 换一个目标 ③ 暂不处理。确认前不会动手。");
    }

    /// <summary>
    /// 复述当前已确认的目标 — 取自目标引擎的运行时状态，而非本轮对话里推测出来的意图。
    /// <para>换模型常常意味着上下文与能力预期都变了。若只打印「已切换模型」，
    /// 用户无从知道上一轮确认的目标是否还算数；而模型自己「记得」的目标可能是幻觉。</para>
    /// </summary>
    private static async Task PrintGoalAsync(ChatCommandContext context) {
        // 直接问容器而不是走 ChatCommandBase.GetService：目标引擎属于可选依赖，
        // 未装配时不该在屏幕上打一行「服务未初始化」的错误。
        var engine = context.Services.GetService<IGoalEngine>();
        if (engine is null) {
            PrintLines(RenderGoal(null, engineAvailable: false));
            return;
        }

        // CurrentState 只在内存里：目标持久化在磁盘、进程重启后要先 Rehydrate 才读得到。
        // 不加载就打印「（无）」等于告诉用户「你没定过目标」—— 那是假话，比不说更糟。
        string? loadError = null;
        if (engine.CurrentState is null) {
            try {
                await engine.RehydrateAsync(context.CancellationToken).ConfigureAwait(false);
            } catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException) {
                loadError = ex.Message;
            }
        }
        PrintLines(RenderGoal(engine.CurrentState, engineAvailable: true, loadError));
    }

    /// <summary>
    /// 把目标状态渲染成要打印的行 —— 纯函数，便于锁住「读不到 ≠ 没有目标」这条界线。
    /// </summary>
    /// <param name="state">当前目标；为空表示确实没有目标。</param>
    /// <param name="engineAvailable">目标引擎是否可用；false 时不得断言「没有目标」。</param>
    /// <param name="loadError">加载失败原因；非空时同样不得断言「没有目标」。</param>
    internal static IReadOnlyList<string> RenderGoal(GoalState? state, bool engineAvailable, string? loadError = null) {
        if (!engineAvailable)
            return [
                "已确认的目标：无法核实（目标引擎未就绪）",
                "  这不等于「没有目标」，也不等于「目标已完成」；请让用户重述目标后再动手。"
            ];

        if (loadError is not null)
            return [
                $"已确认的目标：无法核实（目标状态加载失败：{loadError}）",
                "  这不等于「没有目标」；请让用户重述目标后再动手。"
            ];

        if (state is null)
            return [
                "已确认的目标：（无）",
                "  换模型后请先说明这次要做什么。不要沿用上一轮未确认的推断，也不要假定旧目标仍然有效。"
            ];

        var lines = new List<string>(3) { $"已确认的目标：{state.Objective}" };
        var progress = $"  状态 {state.Status}，已进行 {state.TurnsCompleted} 轮";
        if (state.LastEvaluation is { } evaluation)
            progress += $"；上次评估 {(evaluation.IsCompleted ? "已达成" : "未达成")} — {evaluation.Reason}";
        lines.Add(progress);
        if (state.Constraints.Count > 0)
            lines.Add($"  约束：{string.Join("；", state.Constraints)}");
        return lines;
    }

    private static void PrintLines(IReadOnlyList<string> lines) {
        foreach (var line in lines)
            TerminalHelper.WriteLine(line);
    }

    /// <summary>只列未完成项；全部完成时给结论而不是把整份清单再念一遍。</summary>
    private static void PrintChecklist(Checklist? checklist) {
        if (checklist is null) {
            TerminalHelper.WriteLine("诚实清单未找到（resources/honesty/checklist.yaml）。这不是「全部完成」，而是无法核实。");
            TerminalHelper.WriteLine("修复方式：从仓库根目录确认该文件存在，再用 jcc 启动；不要据此推断能力状态。");
            return;
        }

        var pending = checklist.Items.Where(item => !IsDone(item)).ToArray();
        if (pending.Length == 0) {
            TerminalHelper.WriteLine($"诚实清单：{checklist.Items.Count} 项全部有验证证据，无未完成项（{checklist.Doc}）。");
            return;
        }

        TerminalHelper.WriteLine($"诚实清单：{checklist.Items.Count} 项中 {pending.Length} 项未完成，不会自动执行 ——");
        foreach (var item in pending) {
            TerminalHelper.WriteLine($"  [ ] {item.Title}");
            TerminalHelper.WriteLine($"      证据: {item.Evidence}");
            TerminalHelper.WriteLine($"      下一步: {item.Next}");
        }
        TerminalHelper.WriteLine($"完整清单: {checklist.Doc}");
    }

    private static bool IsDone(Item item) => item.Status.Equals("done", StringComparison.OrdinalIgnoreCase);

    /// <summary>从可执行文件目录向上找仓库根，读 checklist.yaml —— 开发与部署布局都能命中。</summary>
    private static async Task<Checklist?> LoadChecklistAsync(IFileSystem fs) {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent) {
            var candidate = fs.CombinePath(dir.FullName, "resources", "honesty", "checklist.yaml");
            if (!fs.FileExists(candidate)) continue;
            try {
                return Parse(await fs.ReadAllText(candidate).ConfigureAwait(false), candidate);
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException) {
                TerminalHelper.WriteError($"诚实清单读取失败（HonestyNotice 资源层拦截）：{ex.Message}");
                return null;
            }
        }
        return null;
    }

    /// <summary>极简 YAML 解析：只认本文件固定的 doc / items 形状，不引入 YamlDotNet 依赖。</summary>
    private static Checklist Parse(string yaml, string source) {
        var items = new List<Item>();
        var doc = "";
        string? id = null, title = null, status = null, evidence = null, next = null;

        void Flush() {
            if (id is null) return;
            if (string.IsNullOrWhiteSpace(title))
                throw new FormatException($"{source} 条目 '{id}' 缺少 title");
            items.Add(new Item(id, title, status ?? "pending", evidence ?? "无", next ?? "待定"));
            id = title = status = evidence = next = null;
        }

        foreach (var raw in yaml.Split('\n')) {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("doc:", StringComparison.Ordinal)) {
                doc = Unquote(line["doc:".Length..].Trim());
                continue;
            }
            if (line.StartsWith("- id:", StringComparison.Ordinal)) {
                Flush();
                id = Unquote(line["- id:".Length..].Trim());
                continue;
            }
            if (id is null) continue;
            if (line.StartsWith("title:", StringComparison.Ordinal)) title = Unquote(line["title:".Length..].Trim());
            else if (line.StartsWith("status:", StringComparison.Ordinal)) status = Unquote(line["status:".Length..].Trim());
            else if (line.StartsWith("evidence:", StringComparison.Ordinal)) evidence = Unquote(line["evidence:".Length..].Trim());
            else if (line.StartsWith("next:", StringComparison.Ordinal)) next = Unquote(line["next:".Length..].Trim());
        }
        Flush();

        if (items.Count == 0)
            throw new FormatException($"{source} 未定义任何检查项");
        return new Checklist(string.IsNullOrWhiteSpace(doc) ? source : doc, items);
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
}
