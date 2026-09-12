namespace JoinCode.CliCommands;

/// <summary>
/// GitHub 子命令 — <c>jcc gh &lt;group&gt; &lt;action&gt; [位置参数...] [--选项 值] [--json]</c>
/// <para>ADR: 0089 — 禁止系统 gh CLI，GitHub 的 PR/CI 一律走 jcc 自带工具。</para>
/// <para>ADR: 0090 — 扁平元动词形态，工具名按 <c>gh_{group}_{action}</c> 约定拼接，
/// 位置参数按工具 schema 的 required 顺序绑定，执行与输出复用 <see cref="McpCliCommand"/>。</para>
/// </summary>
internal static class GhSubCommand
{
    /// <summary>
    /// 执行 <c>jcc gh ...</c>（<paramref name="args"/>[0] 为子命令名 <c>gh</c>）。
    /// </summary>
    /// <param name="args">完整命令行参数。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>进程退出码：0 成功，1 参数/执行失败。</returns>
    public static async Task<int?> ExecuteAsync(string[] args, CancellationToken ct)
    {
        if (Array.IndexOf(args, CliArgConstants.HelpLongName) >= 0
            || Array.IndexOf(args, CliArgConstants.HelpShortName) >= 0)
        {
            TerminalHelper.WriteLine(GhCommandResolver.Usage);
            return 0;
        }

        var resolved = GhCommandResolver.Resolve(args, out var resolveError);
        if (resolved is null)
        {
            TerminalHelper.WriteError(resolveError!);
            return 1;
        }

        return await McpCliCommand.WithHostAsync(async services =>
        {
            var registry = services.GetRequiredService<IMcpToolRegistry>();
            var info = await registry.GetToolInfoAsync(resolved.ToolName, ct).ConfigureAwait(false);
            if (info is null)
            {
                TerminalHelper.WriteError(await ToolNotFoundMessage(registry, resolved, ct).ConfigureAwait(false));
                return 1;
            }

            var parameters = GhParamSchemaParser.Parse(info.InputSchema);
            var bound = GhArgsBinder.Bind(resolved.Tail, parameters, resolved.ToolName, out var bindError);
            if (bound is null)
            {
                TerminalHelper.WriteError(bindError!);
                return 1;
            }

            var argDict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var (key, value) in bound)
                argDict[key] = McpCliCommand.ParseValueToJsonElement(value, key);

            var result = await registry.ExecuteToolAsync(resolved.ToolName, argDict, ct).ConfigureAwait(false);
            return McpCliCommand.OutputResult(result, resolved.Json);
        }, ct: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 工具不存在时的报错 — 反查同分组下已注册的 <c>gh_*</c> 工具给出可选项（约定大于配置，无静态表）。
    /// </summary>
    private static async Task<string> ToolNotFoundMessage(
        IMcpToolRegistry registry, GhResolvedCommand resolved, CancellationToken ct)
    {
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
}
