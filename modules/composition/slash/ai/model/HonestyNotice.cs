namespace JoinCode.ChatCommands;

/// <summary>
/// 诚实模块 — 用户切换模型后，主动复述「已做什么 / 还缺什么」。
/// <para>存在理由：换模型常常意味着上下文与能力预期都变了。若只打印「已切换模型」，
/// 用户无从知道哪些承诺还没兑现，下一轮很容易把「没做」误当成「做过了」。
/// 本模块只读 <c>resources/honesty/checklist.yaml</c>，不改任务状态、不触发任何后续动作 ——
/// 方向必须由用户决定（TASK083）。</para>
/// <para>清单位于源码仓库而非内嵌资源：它是给人维护、随进度打勾的活文档，
/// 每次切换都读当前内容，避免清单改了却要重编 exe 才生效。</para>
/// </summary>
internal static class HonestyNotice {
    private sealed record Item(string Id, string Title, string Status, string Evidence, string Next);
    private sealed record Checklist(string Doc, IReadOnlyList<Item> Items);

    /// <summary>模型切换成功后调用 —— 打印诚实清单并等待用户决定方向。</summary>
    public static async Task PrintAfterModelSwitchAsync(ChatCommandContext context, string modelId) {
        var fs = ChatCommandBase.GetService<IFileSystem>(context, typeof(IFileSystem));
        var checklist = fs is null ? null : await LoadChecklistAsync(fs).ConfigureAwait(false);

        TerminalHelper.WriteLine($"{TerminalColors.Warning}═══ 诚实提示：模型已切换为 {modelId} ═══{AnsiStyleEnumConstants.Reset}");
        TerminalHelper.WriteLine("换模型后，下列能力可能遗漏。当前状态如实列出，未打勾项不会自动执行：");

        if (checklist is null) {
            TerminalHelper.WriteLine("  [!] 诚实清单未找到（resources/honesty/checklist.yaml）。这不是「全部完成」，而是无法核实。");
            TerminalHelper.WriteLine("  修复方式：从仓库根目录确认该文件存在，再用 jcc 启动；不要据此推断能力状态。");
            return;
        }

        foreach (var item in checklist.Items) {
            var mark = item.Status.Equals("done", StringComparison.OrdinalIgnoreCase) ? "[x]" : "[ ]";
            TerminalHelper.WriteLine($"  {mark} {item.Title}");
            TerminalHelper.WriteLine($"      证据: {item.Evidence}");
            if (!mark.Equals("[x]", StringComparison.Ordinal))
                TerminalHelper.WriteLine($"      下一步: {item.Next}");
        }

        TerminalHelper.WriteLine("请选择方向：① 继续某个未完成项 ② 调整目标 ③ 暂不处理；确认前不会动手。");
        TerminalHelper.WriteLine($"完整清单: {checklist.Doc}");
    }

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
