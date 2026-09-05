namespace JoinCode.CliCommands;

/// <summary>
/// 斜杠命令直调执行器 — jcc slash_call &lt;cmd&gt; &lt;argsJson&gt;
/// <para>ADR: 0069 — 全量暴露，所有斜杠命令都可直调，通过 ISlashCommandRegistry 路由。</para>
/// </summary>
internal static class SlashCallExecutor
{
    public static async Task<int?> ExecuteAsync(string[] args, CancellationToken ct)
    {
        var cmdName = FlatSubCommandRouter.GetPositional(args, 0);
        if (string.IsNullOrEmpty(cmdName))
        {
            TerminalHelper.WriteError("用法: jcc slash_call <cmd> <argsJson>");
            TerminalHelper.WriteError("示例: jcc slash_call compact '{\"level\":2}'");
            return 1;
        }

        cmdName = cmdName.TrimStart('/');
        var argsJson = FlatSubCommandRouter.GetPositional(args, 1) ?? string.Empty;

        return await McpCliCommand.WithHostAsync(async services =>
        {
            var registry = services.GetService<ISlashCommandRegistry>();
            if (registry is null)
            {
                TerminalHelper.WriteError("斜杠命令注册表未注册");
                return 1;
            }

            var command = registry.GetCommand(cmdName);
            if (command is null)
            {
                TerminalHelper.WriteError($"未找到斜杠命令: /{cmdName}（用 jcc slash_list 查看所有命令）");
                return 1;
            }

            var context = new ChatCommandContext
            {
                Arguments = argsJson,
                CancellationToken = ct,
                Services = services,
            };

            await command.ExecuteAsync(context).ConfigureAwait(false);
            return 0;
        }, ct).ConfigureAwait(false);
    }
}

/// <summary>
/// 斜杠命令列表执行器 — jcc slash_list [--category &lt;cat&gt;] [--json]
/// <para>从 GeneratedSlashCommandCatalog 获取命令清单，按分类分组输出。</para>
/// </summary>
internal static class SlashListExecutor
{
    public static Task<int?> ExecuteAsync(string[] args, CancellationToken ct)
    {
        var category = FlatSubCommandRouter.GetOptionValue(args, "--category");
        var json = FlatSubCommandRouter.HasFlag(args, "--json");

        var catalog = new GeneratedSlashCommandCatalog();
        var commands = catalog.Commands.Where(c => !c.IsHidden).ToList();

        if (!string.IsNullOrEmpty(category))
            commands = commands.Where(c => string.Equals(c.Category, category, StringComparison.OrdinalIgnoreCase)).ToList();

        if (json)
        {
            var items = commands.Select(c => new Cli.Output.CliSlashCommandListItem(c.Name, c.Description, c.Usage, c.Category, c.Aliases)).ToList();
            var envelope = Cli.Output.CliOutputEnvelope.Success(items, new Cli.Output.CliOutputMeta { TotalCount = items.Count });
            System.Console.WriteLine(RelaxedJsonSerializer.Serialize(envelope, Cli.Output.CliOutputJsonContext.Default));
        }
        else
        {
            var grouped = commands.GroupBy(c => c.Category).OrderBy(g => g.Key);
            foreach (var g in grouped)
            {
                TerminalHelper.WriteLine($"{TerminalColors.Info}{g.Key}{AnsiStyleConstants.Reset} ({g.Count()} 个):");
                foreach (var cmd in g.OrderBy(c => c.Name))
                    TerminalHelper.WriteLine($"  {cmd.Name,-30} {cmd.Description}");
                TerminalHelper.NewLine();
            }
            TerminalHelper.WriteLine($"总计: {commands.Count} 个命令");
        }

        return Task.FromResult<int?>(0);
    }
}

/// <summary>
/// 斜杠命令参数 schema 查询执行器 — jcc slash_schema &lt;cmd&gt; [--json]
/// <para>从 GeneratedSlashCommandSchemaCatalog 获取参数 schema，和 mcp_schema 统一 ToolSchema 格式。</para>
/// <para>未声明 [ChatCommandArg] 的命令降级输出 ArgumentHint。</para>
/// </summary>
internal static class SlashSchemaExecutor
{
    public static Task<int?> ExecuteAsync(string[] args, CancellationToken ct)
    {
        var cmdName = FlatSubCommandRouter.GetPositional(args, 0);
        if (string.IsNullOrEmpty(cmdName))
        {
            TerminalHelper.WriteError("用法: jcc slash_schema <cmd> [--json]");
            return Task.FromResult<int?>(1);
        }

        cmdName = cmdName.TrimStart('/');
        var json = FlatSubCommandRouter.HasFlag(args, "--json");

        var catalog = new GeneratedSlashCommandSchemaCatalog();
        var entry = catalog.AllSchemas.FirstOrDefault(e => string.Equals(e.CommandName, cmdName, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            TerminalHelper.WriteError($"未找到斜杠命令: /{cmdName}");
            return Task.FromResult<int?>(1);
        }

        if (entry.Schema is not null)
        {
            if (json)
                System.Console.WriteLine(RelaxedJsonSerializer.Serialize(entry.Schema, ContractsJsonContext.Default));
            else
            {
                TerminalHelper.WriteLine($"命令: /{entry.CommandName}");
                TerminalHelper.WriteLine("参数 Schema:");
                System.Console.WriteLine(RelaxedJsonSerializer.Serialize(entry.Schema, ContractsJsonContext.Default));
            }
        }
        else
        {
            if (json)
            {
                var sb = new StringBuilder();
                sb.Append($"{{\"command\":\"{cmdName}\",\"schema\":null,\"argumentHint\":");
                if (string.IsNullOrEmpty(entry.ArgumentHint))
                    sb.Append("null}");
                else
                    sb.Append($"\"{entry.ArgumentHint}\"}}");
                System.Console.WriteLine(sb.ToString());
            }
            else
            {
                TerminalHelper.WriteLine($"命令: /{entry.CommandName}");
                if (!string.IsNullOrEmpty(entry.ArgumentHint))
                    TerminalHelper.WriteLine($"参数提示: {entry.ArgumentHint}");
                else
                    TerminalHelper.WriteLine("（未声明结构化参数 schema，使用 [ChatCommandArg] 特性声明以启用）");
            }
        }

        return Task.FromResult<int?>(0);
    }
}
