namespace JoinCode.CliCommands;

/// <summary>
/// GitHub 子命令 — <c>jcc gh &lt;group&gt; &lt;action&gt; [位置参数...] [--选项 值] [--json]</c>
/// <para>ADR: 0089 — 禁止系统 gh CLI，GitHub 的 PR/CI 一律走 jcc 自带工具。</para>
/// <para>ADR: 0090 — 扁平元动词形态，工具名按 <c>gh_{group}_{action}</c> 约定拼接，
/// 位置参数按工具 schema 的 required 顺序绑定，执行与输出复用 <see cref="McpCliCommand"/>。</para>
/// </summary>
internal static class GhSubCommand {
    /// <summary>
    /// 执行 <c>jcc gh ...</c>（<paramref name="args"/>[0] 为子命令名 <c>gh</c>）。
    /// </summary>
    /// <param name="args">完整命令行参数。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>进程退出码：0 成功，1 参数/执行失败。</returns>
    public static async Task<int?> ExecuteAsync(string[] args, CancellationToken ct) {
        var hasHelp = Array.IndexOf(args, CliArgCliOptionConstants.HelpLongName) >= 0
                    || Array.IndexOf(args, CliArgCliOptionConstants.HelpShortName) >= 0;
        if (hasHelp) {
            var group = FlatSubCommandRouter.GetPositional(args, 0);
            var action = group is not null ? FlatSubCommandRouter.GetPositional(args, 1) : null;
            if (group is not null && action is not null)
                return await RenderToolHelpAsync(group, action, ct).ConfigureAwait(false);
            TerminalHelper.WriteLine(GhCommandResolver.Usage);
            return 0;
        }

        var resolved = GhCommandResolver.Resolve(args, out var resolveError);
        if (resolved is null) {
            TerminalHelper.WriteError(resolveError!);
            return 1;
        }

        return await McpCliCommand.WithHostAsync(async services => {
            var registry = services.GetRequiredService<IMcpToolRegistry>();
            var info = await registry.GetToolInfoAsync(resolved.ToolName, ct).ConfigureAwait(false);
            if (info is null) {
                TerminalHelper.WriteError(await ToolNotFoundMessage(registry, resolved, ct).ConfigureAwait(false));
                return 1;
            }

            var parameters = GhParamSchemaParser.Parse(info.InputSchema);
            var bound = GhArgsBinder.Bind(resolved.Tail, parameters, resolved.ToolName, out var bindError);
            if (bound is null) {
                TerminalHelper.WriteError(bindError!);
                return 1;
            }

            var argDict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var (key, value) in bound)
                argDict[key] = await McpCliCommand.ParseValueToJsonElementAsync(value, key).ConfigureAwait(false);

            var result = await registry.ExecuteToolAsync(resolved.ToolName, argDict, ct).ConfigureAwait(false);
            return McpCliCommand.OutputResult(result, resolved.Json);
        }, ct: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 工具不存在时的报错 — 反查同分组下已注册的 <c>gh_*</c> 工具给出可选项（约定大于配置，无静态表）。
    /// </summary>
    private static async Task<string> ToolNotFoundMessage(
        IMcpToolRegistry registry, GhResolvedCommand resolved, CancellationToken ct) {
        var all = await registry.GetAllToolsAsync(ct).ConfigureAwait(false);
        var prefix = resolved.Action is null
            ? $"gh_{resolved.Group}"
            : $"gh_{resolved.Group}_";
        var candidates = all.Values
            .Where(t => t.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(t => resolved.Action is null ? t.Name : t.Name[prefix.Length..])
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var head = CliErrorCatalog.ResourceNotFound("gh 子命令", resolved.ToolName).ToRustStyleString(resolved.ToolName);
        if (candidates.Count == 0)
            return $"{head}\n提示: 没有以 {prefix} 开头的工具，用 jcc mcp_list --category github 查看全部 GitHub 工具";

        var list = resolved.Action is null
            ? string.Join(", ", candidates)
            : string.Join(", ", candidates);
        return $"{head}\n可用: {list}";
    }

    /// <summary>
    /// 渲染具体工具的动态帮助 — 从 MCP ToolSchema 生成参数说明。
    /// </summary>
    private static async Task<int?> RenderToolHelpAsync(string group, string action, CancellationToken ct) {
        return await McpCliCommand.WithHostAsync(async services => {
            var registry = services.GetRequiredService<IMcpToolRegistry>();
            var toolName = string.Equals(group, "api", StringComparison.OrdinalIgnoreCase)
                ? "gh_api"
                : $"gh_{group}_{action}";
            var info = await registry.GetToolInfoAsync(toolName, ct).ConfigureAwait(false);
            if (info is null) {
                TerminalHelper.WriteLine(GhCommandResolver.Usage);
                return 0;
            }
            GhToolHelpRenderer.WriteToTerminal(info, group, action);
            return 0;
        }, ct: ct).ConfigureAwait(false);
    }
}

/// <summary>
/// gh 工具帮助渲染器 — 从 ToolInfo + ToolSchema 动态生成人类可读的参数说明。
/// <para>替代硬编码 Usage：jcc gh &lt;group&gt; &lt;action&gt; --help 时从 MCP schema 实时生成。</para>
/// </summary>
internal static class GhToolHelpRenderer {
    /// <summary>
    /// 生成工具帮助文本 — 纯函数，不直接写 stdout，便于单元测试。
    /// </summary>
    /// <param name="info">MCP 工具信息（含 Name/Description/InputSchema）。</param>
    /// <param name="group">gh 分组名（如 pr/issue/repo）。</param>
    /// <param name="action">gh 动作名（如 view/list）。</param>
    /// <returns>多行帮助文本</returns>
    internal static string Render(Abstractions.Tools.ToolInfo info, string group, string action) {
        var sb = new System.Text.StringBuilder(512);
        sb.AppendLine($"工具: {info.Name}");
        if (!string.IsNullOrEmpty(info.Description))
            sb.AppendLine($"描述: {info.Description}");
        if (!string.IsNullOrEmpty(info.Category))
            sb.AppendLine($"分类: {info.Category}");
        sb.AppendLine();

        var schema = info.InputSchema;
        var required = schema.Required;
        var optionalProps = schema.Properties
            .Where(p => !required.Contains(p.Key))
            .ToList();

        var usageParts = new List<string> { "jcc", "gh", group, action };
        foreach (var r in required)
            usageParts.Add($"<{r}>");
        foreach (var (name, _) in optionalProps)
            usageParts.Add($"[--{name}]");
        usageParts.Add("[--json]");
        sb.AppendLine($"用法: {string.Join(' ', usageParts)}");

        if (required.Count > 0) {
            sb.AppendLine();
            sb.AppendLine("必填参数:");
            foreach (var r in required)
                if (schema.Properties.TryGetValue(r, out var prop))
                    sb.AppendLine($"  <{r}>    {prop.Description ?? ""}");
        }

        if (optionalProps.Count > 0) {
            sb.AppendLine();
            sb.AppendLine("可选参数:");
            foreach (var (name, prop) in optionalProps) {
                var valueHint = string.Equals(prop.Type, "boolean", StringComparison.OrdinalIgnoreCase)
                    ? ""
                    : " <值>";
                sb.AppendLine($"  --{name}{valueHint}    {prop.Description ?? ""}");
            }
        }

        sb.AppendLine();
        sb.Append($"完整 schema: jcc mcp_schema {info.Name}");
        return sb.ToString();
    }

    /// <summary>
    /// 渲染工具帮助到 stdout — 调用纯函数 Render 后逐行输出。
    /// </summary>
    internal static void WriteToTerminal(Abstractions.Tools.ToolInfo info, string group, string action)
        => TerminalHelper.WriteLine(Render(info, group, action));
}