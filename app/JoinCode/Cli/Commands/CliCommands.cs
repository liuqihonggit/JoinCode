namespace JoinCode.CliCommands;

/// <summary>
/// 工具命令 - 提供命令行工具执行功能
/// </summary>
public sealed class ToolCommand : Command
{
    private static readonly Cli.Output.CliOutputJsonContext JsonCtx = Cli.Output.CliOutputJsonContext.Default;

    public ToolCommand() : base("tool", "执行本地工具")
    {
        var jsonOption = new Option<bool>(JccCliArgConstants.Json) { Description = "以 JSON 格式输出结果" };
        var listCommand = new Command("list", "列出所有可用工具");
        listCommand.Add(jsonOption);
        listCommand.SetAction(parseResult =>
        {
            var json = parseResult.GetValue(jsonOption);
            if (json)
            {
                var categories = new List<Dictionary<string, object>>
                {
                    new() { ["category"] = "文件操作", ["tools"] = "Read / Write / Edit / Glob / Grep" },
                    new() { ["category"] = "代码执行", ["tools"] = "Bash / PowerShell / REPL / ExecuteCSharpCode" },
                    new() { ["category"] = "搜索", ["tools"] = "SearchCodebase / SearchFiles / SymbolSearch" },
                    new() { ["category"] = "代码索引", ["tools"] = "CodeIndex 系列 (Explore/FindDefinition/FindReferences)" },
                    new() { ["category"] = "MCP", ["tools"] = "MCP 工具管理 (Connect/Disconnect/ListTools)" },
                    new() { ["category"] = "代理", ["tools"] = "Agent / Team / Task 系列" },
                    new() { ["category"] = "语音", ["tools"] = "Voice (StartRecording/StopRecording/Transcribe)" },
                    new() { ["category"] = "记忆", ["tools"] = "Memory 系列 (Scan/Search/Health)" },
                    new() { ["category"] = "其他", ["tools"] = "Voice / Notebook / LSP / Web 等" },
                };
                var envelope = Cli.Output.CliOutputEnvelope.Success(categories, new Cli.Output.CliOutputMeta
                {
                    TotalCount = categories.Count,
                });
                var jsonStr = System.Text.Json.JsonSerializer.Serialize(envelope, JsonCtx.CliOutputEnvelope);
                System.Console.WriteLine(jsonStr);
            }
            else
            {
                TerminalHelper.WriteLine("JoinCode 工具分类:");
                TerminalHelper.NewLine();
                TerminalHelper.WriteLine("  文件操作    Read / Write / Edit / Glob / Grep");
                TerminalHelper.WriteLine("  代码执行    Bash / PowerShell / REPL / ExecuteCSharpCode");
                TerminalHelper.WriteLine("  搜索        SearchCodebase / SearchFiles / SymbolSearch");
                TerminalHelper.WriteLine("  代码索引    CodeIndex 系列 (Explore/FindDefinition/FindReferences)");
                TerminalHelper.WriteLine("  MCP         MCP 工具管理 (Connect/Disconnect/ListTools)");
                TerminalHelper.WriteLine("  代理        Agent / Team / Task 系列");
                TerminalHelper.WriteLine("  语音        Voice (StartRecording/StopRecording/Transcribe)");
                TerminalHelper.WriteLine("  记忆        Memory 系列 (Scan/Search/Health)");
                TerminalHelper.WriteLine("  其他        Voice / Notebook / LSP / Web 等");
                TerminalHelper.NewLine();
                TerminalHelper.WriteLine("提示: 在交互模式下使用 /tools 命令查看完整工具列表（含参数说明）");
            }
        });

        var infoCommand = new Command("info", "显示工具详细信息");
        var infoToolArgument = new Argument<string>("tool-name") { Description = "工具名称" };
        infoCommand.Add(infoToolArgument);
        infoCommand.Add(jsonOption);
        infoCommand.SetAction(parseResult =>
        {
            var toolName = parseResult.GetValue(infoToolArgument);
            var json = parseResult.GetValue(jsonOption);
            if (json)
            {
                var envelope = Cli.Output.CliOutputEnvelope.Success(new { toolName, hint = "请在交互模式下使用 /tools 查看详细信息" });
                var jsonStr = System.Text.Json.JsonSerializer.Serialize(envelope, JsonCtx.CliOutputEnvelope);
                System.Console.WriteLine(jsonStr);
            }
            else
            {
                TerminalHelper.WriteLine($"工具 '{toolName}' 的详细信息请在交互模式下使用 /tools 命令查看");
            }
        });

        var execCommand = new Command("exec", "执行指定工具");
        var execToolArgument = new Argument<string>("tool-name") { Description = "要执行的工具名称" };
        var execArgsOption = new Option<string?>("--args") { Description = "工具参数" };
        execCommand.Add(execToolArgument);
        execCommand.Add(execArgsOption);
        execCommand.Add(jsonOption);
        execCommand.SetAction(parseResult =>
        {
            var toolName = parseResult.GetValue(execToolArgument);
            var args = parseResult.GetValue(execArgsOption);
            var json = parseResult.GetValue(jsonOption);
            if (json)
            {
                var envelope = Cli.Output.CliOutputEnvelope.Success(new { toolName, args, hint = "请在交互模式下调用工具" });
                var jsonStr = System.Text.Json.JsonSerializer.Serialize(envelope, JsonCtx.CliOutputEnvelope);
                System.Console.WriteLine(jsonStr);
            }
            else
            {
                TerminalHelper.WriteLine($"工具 '{toolName}' 需在交互模式下调用");
                TerminalHelper.WriteLine("请运行 jcc 进入交互模式，输入自然语言描述任务，AI 会自动选择并调用合适的工具");
                if (!string.IsNullOrEmpty(args))
                {
                    TerminalHelper.WriteLine($"参数: {args}");
                }
            }
        });

        Add(listCommand);
        Add(infoCommand);
        Add(execCommand);
    }
}

/// <summary>
/// Agent 命令 - 提供智能体管理和执行功能
/// </summary>
public sealed class AgentCommand : Command
{
    private static readonly Cli.Output.CliOutputJsonContext JsonCtx = Cli.Output.CliOutputJsonContext.Default;
    private readonly IFileSystem _fs;

    public AgentCommand(IFileSystem fs) : base("agent", "管理和执行AI智能体")
    {
        _fs = fs;
        var jsonOption = new Option<bool>(JccCliArgConstants.Json) { Description = "以 JSON 格式输出结果" };
        var runCommand = new Command("run", "运行指定智能体");
        var runAgentArgument = new Argument<string>("agent-name") { Description = "要运行的智能体名称" };
        var runInputOption = new Option<string?>("--input") { Description = "智能体输入/提示词" };
        runCommand.Add(runAgentArgument);
        runCommand.Add(runInputOption);
        runCommand.Add(jsonOption);
        runCommand.SetAction(parseResult =>
        {
            var agentName = parseResult.GetValue(runAgentArgument);
            var input = parseResult.GetValue(runInputOption);
            var json = parseResult.GetValue(jsonOption);
            if (json)
            {
                var envelope = Cli.Output.CliOutputEnvelope.Success(new { agentName, input, status = "pending" });
                var jsonStr = System.Text.Json.JsonSerializer.Serialize(envelope, JsonCtx.CliOutputEnvelope);
                System.Console.WriteLine(jsonStr);
            }
            else
            {
                TerminalHelper.WriteLine($"运行智能体: {agentName}");
                if (!string.IsNullOrEmpty(input))
                {
                    TerminalHelper.WriteLine($"输入: {input}");
                }
                TerminalHelper.NewLine();
                TerminalHelper.WriteLine("[智能体执行功能将在后续版本中实现]");
            }
        });

        var listCommand = new Command("list", "列出所有可用智能体");
        listCommand.Add(jsonOption);
        listCommand.SetAction(parseResult =>
        {
            var json = parseResult.GetValue(jsonOption);
            var agents = new List<Dictionary<string, string>>
            {
                new() { ["name"] = "plan", ["description"] = "计划智能体" },
                new() { ["name"] = "explore", ["description"] = "探索智能体" },
                new() { ["name"] = "verify", ["description"] = "验证智能体" },
                new() { ["name"] = "general", ["description"] = "通用智能体" },
            };
            if (json)
            {
                var envelope = Cli.Output.CliOutputEnvelope.Success(agents, new Cli.Output.CliOutputMeta
                {
                    TotalCount = agents.Count,
                });
                var jsonStr = System.Text.Json.JsonSerializer.Serialize(envelope, JsonCtx.CliOutputEnvelope);
                System.Console.WriteLine(jsonStr);
            }
            else
            {
                TerminalHelper.WriteLine("可用智能体列表:");
                TerminalHelper.NewLine();
                TerminalHelper.WriteLine("  plan - 计划智能体");
                TerminalHelper.WriteLine("  explore - 探索智能体");
                TerminalHelper.WriteLine("  verify - 验证智能体");
                TerminalHelper.WriteLine("  general - 通用智能体");
            }
        });

        Add(runCommand);
        Add(listCommand);
    }
}

/// <summary>
/// 代码命令 - 提供代码分析、生成和执行功能
/// </summary>
public sealed class CodeCommand : Command
{
    private static readonly Cli.Output.CliOutputJsonContext JsonCtx = Cli.Output.CliOutputJsonContext.Default;
    private readonly IFileSystem _fs;

    public CodeCommand(IFileSystem fs) : base("code", "代码分析、生成和执行工具")
    {
        _fs = fs;
        var jsonOption = new Option<bool>(JccCliArgConstants.Json) { Description = "以 JSON 格式输出结果" };
        var analyzeCommand = new Command("analyze", "分析代码文件或目录");
        var analyzePathArgument = new Argument<string>("path") { Description = "要分析的代码文件或目录路径" };
        analyzeCommand.Add(analyzePathArgument);
        analyzeCommand.Add(jsonOption);
        analyzeCommand.SetAction(parseResult =>
        {
            var path = parseResult.GetValue(analyzePathArgument);
            var json = parseResult.GetValue(jsonOption);
            if (json)
            {
                var envelope = Cli.Output.CliOutputEnvelope.Success(new { path, hint = "请在交互模式下使用 code_index_explore 工具" });
                var jsonStr = System.Text.Json.JsonSerializer.Serialize(envelope, JsonCtx.CliOutputEnvelope);
                System.Console.WriteLine(jsonStr);
            }
            else
            {
                TerminalHelper.WriteLine($"代码分析路径: {path}");
                TerminalHelper.WriteLine("请在交互模式下使用 code_index_explore 工具进行深度代码分析");
            }
        });

        var searchCommand = new Command("search", "在代码库中搜索");
        var searchQueryArgument = new Argument<string>("query") { Description = "搜索查询" };
        searchCommand.Add(searchQueryArgument);
        searchCommand.Add(jsonOption);
        searchCommand.SetAction(parseResult =>
        {
            var query = parseResult.GetValue(searchQueryArgument);
            var json = parseResult.GetValue(jsonOption);
            if (json)
            {
                var envelope = Cli.Output.CliOutputEnvelope.Success(new { query, hint = "请在交互模式下使用 code_index_search 工具" });
                var jsonStr = System.Text.Json.JsonSerializer.Serialize(envelope, JsonCtx.CliOutputEnvelope);
                System.Console.WriteLine(jsonStr);
            }
            else
            {
                TerminalHelper.WriteLine($"代码搜索: {query}");
                TerminalHelper.WriteLine("请在交互模式下使用 code_index_search 或 search_code 工具进行代码搜索");
            }
        });

        Add(analyzeCommand);
        Add(searchCommand);
    }
}
