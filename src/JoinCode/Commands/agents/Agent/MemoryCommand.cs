namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Memory, Description = "编辑记忆文件", Usage = "/memory [edit|open|add|search|db|stats|health|cleanup]", Category = ChatCommandCategory.Agent)]
public sealed class MemoryCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Memory;
    public string Description => "编辑记忆文件";
    public string Usage => "/memory [edit|open|add|search|db|stats|health|cleanup]";
    public string[] Aliases => ["mem"];
    public string ArgumentHint => string.Empty;
    public bool IsHidden => false;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = ChatCommandBase.GetSplitArgs(context);
        var action = args.Length > 0 ? args[0].ToLowerInvariant() : null;

        if (action is null)
        {
            await ListMemoryFilesAsync(context).ConfigureAwait(false);
            return ChatCommandResult.Continue();
        }

        switch (action)
        {
            case MemorySubCommandConstants.Edit:
                await EditMemoryFileAsync(args, context).ConfigureAwait(false);
                break;
            case MemorySubCommandConstants.Open:
                await OpenMemoryDirectory(context.Services!.FileSystem, ChatCommandBase.GetService<IProcessService>(context)!).ConfigureAwait(false);
                break;
            case MemorySubCommandConstants.Add:
                await AddMemoryAsync(context, args).ConfigureAwait(false);
                break;
            case MemorySubCommandConstants.Search:
                await SearchMemoryAsync(context, args).ConfigureAwait(false);
                break;
            case MemorySubCommandConstants.Db:
                await ListMemoriesAsync(context, args).ConfigureAwait(false);
                break;
            case MemorySubCommandConstants.Stats:
                await ShowStatsAsync(context).ConfigureAwait(false);
                break;
            case MemorySubCommandConstants.Health:
                await ShowHealthAsync(context).ConfigureAwait(false);
                break;
            case MemorySubCommandConstants.Cleanup:
                await CleanupAsync(context, args).ConfigureAwait(false);
                break;
            default:
                TerminalHelper.WriteLine($"{TerminalColors.Error}{L.T(StringKey.HostMemoryUnknownAction, action)}{AnsiStyleConstants.Reset}");
                TerminalHelper.WriteLine(L.T(StringKey.HostMemoryAvailableActions, string.Join(", ", Enum.GetValues<MemorySubCommand>().Select(v => v.ToValue()))));
                break;
        }

        return ChatCommandResult.Continue();
    }

    /// <summary>
    /// 列出记忆文件（交互式选择器）
    /// 对齐 TS: MemoryCommand — Dialog + MemoryFileSelector
    /// </summary>
    private static async Task ListMemoryFilesAsync(ChatCommandContext context)
    {
        var files = GetMemoryFilePaths(context.Services!.FileSystem);

        // 交互模式：使用 Selector 组件
        // 对齐 TS: MemoryFileSelector — 上下键选择记忆文件+Enter编辑+Esc取消
        if (!Core.Utils.TestEnvironmentDetector.IsNonInteractive)
        {
            var selector = new Selector<(string Label, string Path, string Description, bool Exists)>(
                "记忆文件",
                [.. files],
                f => f.Label + (f.Exists ? "" : " (new)"),
                f => f.Description,
                enableSearch: false);

            var result = await selector.ShowAsync(context.CancellationToken).ConfigureAwait(false);

            if (result.Cancelled || result.Selected.Equals(default))
            {
                TerminalHelper.WriteLine(L.T(StringKey.HostMemoryCancelled));
                return;
            }

            // 选择后打开编辑器
            EnsureFileExists(result.Selected.Path, context.Services!.FileSystem);
            await OpenInEditor(result.Selected.Path, ChatCommandBase.GetService<IProcessService>(context)).ConfigureAwait(false);
            return;
        }

        // 非交互模式回退：纯文本列表
        TerminalHelper.WriteLine($"{AnsiStyleConstants.Bold}{L.T(StringKey.HostMemoryFilesHeader)}{AnsiStyleConstants.Reset}");
        TerminalHelper.NewLine();

        for (int i = 0; i < files.Count; i++)
        {
            var (label, _, desc, exists) = files[i];
            var existsLabel = exists ? "" : " (new)";
            var color = exists ? TerminalColors.Success : TerminalColors.Muted;
            TerminalHelper.WriteLine($"  {AnsiStyleConstants.Bold}{i + 1}.{AnsiStyleConstants.Reset} {color}{label}{existsLabel}{AnsiStyleConstants.Reset}");
            TerminalHelper.WriteLine($"     {TerminalColors.Muted}{desc}{AnsiStyleConstants.Reset}");
        }

        TerminalHelper.NewLine();
        TerminalHelper.WriteLine($"{TerminalColors.Muted}{L.T(StringKey.HostMemoryEditHint)}{AnsiStyleConstants.Reset}");
        TerminalHelper.WriteLine($"{TerminalColors.Muted}{L.T(StringKey.HostMemoryDbHint)}{AnsiStyleConstants.Reset}");
    }

    /// <summary>
    /// 编辑记忆文件（交互式选择器）
    /// 对齐 TS: MemoryFileSelector — 选择文件后打开编辑器
    /// </summary>
    private static async Task EditMemoryFileAsync(string[] args, ChatCommandContext context)
    {
        var files = GetMemoryFilePaths(context.Services!.FileSystem);

        // 有明确参数时直接打开
        if (args.Length >= 2 && int.TryParse(args[1], out var index) && index >= 1 && index <= files.Count)
        {
            var file = files[index - 1];
            EnsureFileExists(file.Path, context.Services!.FileSystem);
            await OpenInEditor(file.Path, ChatCommandBase.GetService<IProcessService>(context)).ConfigureAwait(false);
            return;
        }

        // 有路径参数时直接打开
        if (args.Length >= 2 && !int.TryParse(args[1], out _))
        {
            var cwd = Environment.CurrentDirectory;
            var path = args[1];
            if (!Path.IsPathRooted(path))
                path = Path.Combine(cwd, path);
            EnsureFileExists(path, context.Services!.FileSystem);
            await OpenInEditor(path, ChatCommandBase.GetService<IProcessService>(context)).ConfigureAwait(false);
            return;
        }

        // 无参数：交互式选择
        if (!Core.Utils.TestEnvironmentDetector.IsNonInteractive)
        {
            var selector = new Selector<(string Label, string Path, string Description, bool Exists)>(
                "选择要编辑的记忆文件",
                [.. files],
                f => f.Label + (f.Exists ? "" : " (new)"),
                f => f.Description,
                enableSearch: false);

            var result = await selector.ShowAsync(context.CancellationToken).ConfigureAwait(false);

            if (result.Cancelled || result.Selected.Equals(default))
            {
                TerminalHelper.WriteLine(L.T(StringKey.HostMemoryCancelled));
                return;
            }

            EnsureFileExists(result.Selected.Path, context.Services!.FileSystem);
            await OpenInEditor(result.Selected.Path, ChatCommandBase.GetService<IProcessService>(context)).ConfigureAwait(false);
            return;
        }

        // 非交互模式回退：打开第一个
        var userPath = files[0].Path;
        EnsureFileExists(userPath, context.Services!.FileSystem);
        await OpenInEditor(userPath, ChatCommandBase.GetService<IProcessService>(context)).ConfigureAwait(false);
    }

    private static async Task OpenMemoryDirectory(IFileSystem fs, IProcessService processService)
    {
        var memDir = Path.Combine(WorkflowConstants.Paths.JccDirectory, "memories");
        DirectoryHelper.EnsureDirectoryExists(fs, memDir);

        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Info}{L.T(StringKey.HostMemoryDirLabel, memDir)}{AnsiStyleConstants.Reset}");
            return;
        }

        try
        {
            await processService.OpenAsync(memDir).ConfigureAwait(false);
            TerminalHelper.WriteLine($"{TerminalColors.Success}{L.T(StringKey.HostMemoryDirOpened, memDir)}{AnsiStyleConstants.Reset}");
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("打开目录", ex);
            TerminalHelper.WriteLine(L.T(StringKey.HostMemoryDirPath, memDir));
        }
    }

    private static async Task OpenInEditor(string filePath, IProcessService? processService = null)
    {
        var editor = Environment.GetEnvironmentVariable("EDITOR")
            ?? Environment.GetEnvironmentVariable("VISUAL")
            ?? "notepad";

        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Info}{L.T(StringKey.HostMemoryOpenEditorHint, editor, filePath)}{AnsiStyleConstants.Reset}");
            return;
        }

        try
        {
            if (processService != null)
            {
                await processService.OpenAsync(filePath).ConfigureAwait(false);
            }
            else
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = editor,
                    Arguments = filePath,
                    UseShellExecute = true
                });
            }
            TerminalHelper.WriteLine($"{TerminalColors.Success}{L.T(StringKey.HostMemoryOpenedInEditor, filePath)}{AnsiStyleConstants.Reset}");
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("打开编辑器", ex);
            TerminalHelper.WriteLine(L.T(StringKey.HostMemoryFilePath, filePath));
        }
    }

    private static List<(string Label, string Path, string Description, bool Exists)> GetMemoryFilePaths(IFileSystem fs)
    {
        var homeDir = WorkflowConstants.Paths.JccDirectory;
        var cwd = Environment.CurrentDirectory;
        var files = new List<(string Label, string Path, string Description, bool Exists)>();

        var userRulesPath = Path.Combine(homeDir, AppDataConstants.RulesFolderName, AppDataConstants.ProjectRulesFileName);
        files.Add(("User memory", userRulesPath, $"~/{AppDataConstants.AppDataFolder}/{AppDataConstants.RulesFolderName}/{AppDataConstants.ProjectRulesFileName}", fs.FileExists(userRulesPath)));

        var projectRulesPath = Path.Combine(cwd, AppDataConstants.AppDataFolder, AppDataConstants.RulesFolderName, AppDataConstants.ProjectRulesFileName);
        files.Add(("Project memory", projectRulesPath, $"./{AppDataConstants.AppDataFolder}/{AppDataConstants.RulesFolderName}/{AppDataConstants.ProjectRulesFileName}", fs.FileExists(projectRulesPath)));

        var agentsPath = Path.Combine(cwd, "AGENTS.md");
        files.Add(("Project AGENTS", agentsPath, "./AGENTS.md", fs.FileExists(agentsPath)));

        var claudePath = Path.Combine(cwd, "CLAUDE.md");
        files.Add(("Project CLAUDE", claudePath, "./CLAUDE.md", fs.FileExists(claudePath)));

        var autoMemDir = Path.Combine(homeDir, AppDataConstants.AppDataFolder, "memories");
        files.Add(("Auto-memory folder", autoMemDir, $"~/{AppDataConstants.AppDataFolder}/memories/", fs.DirectoryExists(autoMemDir)));

        return files;
    }

    private static void EnsureFileExists(string path, IFileSystem fs)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir is not null && !fs.DirectoryExists(dir))
            DirectoryHelper.EnsureDirectoryExists(fs, dir);
        if (!fs.FileExists(path))
            fs.WriteAllText(path, "");
    }

    private static async Task AddMemoryAsync(ChatCommandContext context, string[] args)
    {
        if (args.Length < 2)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}{L.T(StringKey.HostMemoryAddUsage)}{AnsiStyleConstants.Reset}");
            return;
        }

        var memService = context.Services!.MemoryManagementService;
        if (memService is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}{L.T(StringKey.HostMemoryServiceUnavailable)}{AnsiStyleConstants.Reset}");
            return;
        }

        var content = string.Join(" ", args[1..]);
        var type = MemoryType.User;
        var tags = new List<string>();

        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--type" && i + 1 < args.Length)
            {
                var parsed = MemoryTypeExtensions.FromValue(args[i + 1]);
                if (parsed is not null)
                    type = parsed.Value;
                i++;
            }
            else if (args[i] == "--tags" && i + 1 < args.Length)
            {
                tags = args[i + 1].Split(',').Select(t => t.Trim()).ToList();
                i++;
            }
        }

        content = content.Replace($"--type {type}", "").Replace($"--tags {string.Join(",", tags)}", "").Trim();

        if (string.IsNullOrWhiteSpace(content))
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}{L.T(StringKey.HostMemoryContentEmpty)}{AnsiStyleConstants.Reset}");
            return;
        }

        var scanResult = await memService.ScanMemoriesAsync(content, type.GetName(), limit: 1, ct: context.CancellationToken).ConfigureAwait(false);
        if (scanResult.RelevantMemories.Count > 0 && scanResult.RelevantMemories[0].RelevanceScore > 5.0)
        {
            var existing = scanResult.RelevantMemories[0];
            TerminalHelper.WriteLine($"{TerminalColors.Warning}{L.T(StringKey.HostMemorySimilarFound, existing.Memory.Id)}{AnsiStyleConstants.Reset}");
            TerminalHelper.WriteLine($"  {existing.Memory.Content}");
            TerminalHelper.WriteLine(L.T(StringKey.HostMemoryScoreLabel, existing.RelevanceScore));
            return;
        }

        try
        {
            var memoryId = await memService.AddMemoryAsync(content, type, tags: tags.Count > 0 ? tags : null, ct: context.CancellationToken).ConfigureAwait(false);
            TerminalHelper.WriteLine($"{TerminalColors.Success}{L.T(StringKey.HostMemoryAdded, memoryId)}{AnsiStyleConstants.Reset}");
            TerminalHelper.WriteLine(L.T(StringKey.HostMemoryTypeLabel, type.GetName()));
            if (tags.Count > 0)
                TerminalHelper.WriteLine(L.T(StringKey.HostMemoryTagsLabel, string.Join(", ", tags)));
            TerminalHelper.WriteLine(L.T(StringKey.HostMemoryContentLabel, content));
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("添加记忆", ex);
        }
    }

    private static async Task SearchMemoryAsync(ChatCommandContext context, string[] args)
    {
        if (args.Length < 2)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}{L.T(StringKey.HostMemorySearchUsage)}{AnsiStyleConstants.Reset}");
            return;
        }

        var memService = context.Services!.MemoryManagementService;
        if (memService is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}{L.T(StringKey.HostMemoryServiceUnavailable)}{AnsiStyleConstants.Reset}");
            return;
        }

        var query = args[1];
        var category = args.Length > 3 && args[2] == "--type" ? args[3] : null;

        TerminalHelper.WriteLine(L.T(StringKey.HostMemorySearchHeader, query));

        var result = await memService.ScanMemoriesAsync(query, category, limit: 10, ct: context.CancellationToken).ConfigureAwait(false);

        if (result.RelevantMemories.Count == 0)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Muted}{L.T(StringKey.HostMemoryNoResults)}{AnsiStyleConstants.Reset}");
            return;
        }

        foreach (var scored in result.RelevantMemories)
        {
            var m = scored.Memory;
            TerminalHelper.WriteLine($"[{m.Id}] [{m.Type.GetName()}] {L.T(StringKey.HostMemoryScoreLabel, scored.RelevanceScore)}");
            TerminalHelper.WriteLine($"  {m.Content}");
            if (m.Tags.Count > 0)
                TerminalHelper.WriteLine(L.T(StringKey.HostMemoryTagsLabel, string.Join(", ", m.Tags)));
            if (scored.MatchReason is not null)
                TerminalHelper.WriteLine(L.T(StringKey.HostMemoryMatchReasonLabel, scored.MatchReason));
            TerminalHelper.NewLine();
        }

        TerminalHelper.WriteLine($"{TerminalColors.Muted}{L.T(StringKey.HostMemoryFoundRelated, result.TotalMemories)}{AnsiStyleConstants.Reset}");
    }

    private static async Task ListMemoriesAsync(ChatCommandContext context, string[] args)
    {
        var memService = context.Services!.MemoryManagementService;
        if (memService is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}{L.T(StringKey.HostMemoryServiceUnavailable)}{AnsiStyleConstants.Reset}");
            return;
        }

        var category = args.Length > 1 ? args[1] : null;
        TerminalHelper.WriteLine(category is not null ? L.T(StringKey.HostMemoryCategoryHeader, category) : L.T(StringKey.HostMemoryDbHeader));
        TerminalHelper.NewLine();

        var result = await memService.ScanMemoriesAsync("*", category, limit: 20, ct: context.CancellationToken).ConfigureAwait(false);

        if (result.RelevantMemories.Count == 0)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Muted}{L.T(StringKey.HostMemoryNoMemories)}{AnsiStyleConstants.Reset}");
            return;
        }

        foreach (var scored in result.RelevantMemories)
        {
            var m = scored.Memory;
            TerminalHelper.WriteLine($"[{m.Id}] [{m.Type.GetName()}] {m.CreatedAt:yyyy-MM-dd}");
            TerminalHelper.WriteLine($"  {m.Content}");
            if (m.Tags.Count > 0)
                TerminalHelper.WriteLine(L.T(StringKey.HostMemoryTagsLabel, string.Join(", ", m.Tags)));
            TerminalHelper.NewLine();
        }

        TerminalHelper.WriteLine($"{TerminalColors.Muted}{L.T(StringKey.HostMemoryTotal, result.TotalMemories)}{AnsiStyleConstants.Reset}");
    }

    private static async Task ShowStatsAsync(ChatCommandContext context)
    {
        var memService = context.Services!.MemoryManagementService;
        if (memService is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}{L.T(StringKey.HostMemoryServiceUnavailable)}{AnsiStyleConstants.Reset}");
            return;
        }

        TerminalHelper.WriteLine(L.T(StringKey.HostMemoryStatsHeader) + "\n");

        var health = await memService.GetHealthReportAsync(context.CancellationToken).ConfigureAwait(false);

        TerminalHelper.WriteLine(L.T(StringKey.HostMemoryTotalCount, health.TotalMemories));
        TerminalHelper.WriteLine(L.T(StringKey.HostMemoryHealthyCount, health.HealthyMemories));
        TerminalHelper.WriteLine(L.T(StringKey.HostMemoryNeedsAttention, health.NeedsAttention));
        TerminalHelper.WriteLine(L.T(StringKey.HostMemorySuggestArchive, health.ShouldArchive));
        TerminalHelper.WriteLine(L.T(StringKey.HostMemorySuggestDelete, health.ShouldDelete));
        TerminalHelper.WriteLine(L.T(StringKey.HostMemoryAvgHealth, health.AverageHealthScore));

        if (health.AgeDistribution.Count > 0)
        {
            TerminalHelper.WriteLine(L.T(StringKey.HostMemoryAgeDistHeader));
            foreach (var (range, count) in health.AgeDistribution)
            {
                TerminalHelper.WriteLine($"  {range}: {count}");
            }
        }
    }

    private static async Task ShowHealthAsync(ChatCommandContext context)
    {
        var memService = context.Services!.MemoryManagementService;
        if (memService is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}{L.T(StringKey.HostMemoryServiceUnavailable)}{AnsiStyleConstants.Reset}");
            return;
        }

        TerminalHelper.WriteLine(L.T(StringKey.HostMemoryHealthReportHeader) + "\n");

        var health = await memService.GetHealthReportAsync(context.CancellationToken).ConfigureAwait(false);
        var ageInfos = await memService.GetMemoryAgeInfoAsync(context.CancellationToken).ConfigureAwait(false);

        TerminalHelper.WriteLine(L.T(StringKey.HostMemoryTotalCount, health.TotalMemories));
        TerminalHelper.WriteLine(L.T(StringKey.HostMemoryHealthScore, health.AverageHealthScore));

        var healthBar = new string('█', (int)(health.AverageHealthScore / 5)) + new string('░', 20 - (int)(health.AverageHealthScore / 5));
        var barColor = health.AverageHealthScore >= 70 ? TerminalColors.Success
            : health.AverageHealthScore >= 40 ? TerminalColors.Warning
            : TerminalColors.Error;
        TerminalHelper.WriteLine($"[{barColor}{healthBar}{AnsiStyleConstants.Reset}]");

        if (health.ShouldArchive > 0 || health.ShouldDelete > 0)
        {
            TerminalHelper.WriteLine($"\n{TerminalColors.Warning}{L.T(StringKey.HostMemorySuggestions)}{AnsiStyleConstants.Reset}");
            if (health.ShouldArchive > 0)
                TerminalHelper.WriteLine(L.T(StringKey.HostMemorySuggestArchiveMsg, health.ShouldArchive));
            if (health.ShouldDelete > 0)
                TerminalHelper.WriteLine(L.T(StringKey.HostMemorySuggestDeleteMsg, health.ShouldDelete));
            TerminalHelper.WriteLine(L.T(StringKey.HostMemoryCleanupHint));
        }

        if (ageInfos.Count > 0)
        {
            TerminalHelper.WriteLine(L.T(StringKey.HostMemoryMostActiveHeader));
            foreach (var info in ageInfos.OrderByDescending(a => a.AccessCount).Take(5))
            {
                TerminalHelper.WriteLine(L.T(StringKey.HostMemoryActiveInfo, info.MemoryId, info.AccessCount, info.HealthScore));
            }
        }
    }

    private static async Task CleanupAsync(ChatCommandContext context, string[] args)
    {
        var memService = context.Services!.MemoryManagementService;
        if (memService is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}{L.T(StringKey.HostMemoryServiceUnavailable)}{AnsiStyleConstants.Reset}");
            return;
        }

        int? archiveDays = null;
        int? deleteDays = null;

        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--archive-days" && i + 1 < args.Length && int.TryParse(args[i + 1], out var ad))
            {
                archiveDays = ad;
                i++;
            }
            else if (args[i] == "--delete-days" && i + 1 < args.Length && int.TryParse(args[i + 1], out var dd))
            {
                deleteDays = dd;
                i++;
            }
        }

        TerminalHelper.WriteLine(L.T(StringKey.HostMemoryCleaning));

        var result = await memService.CleanupOldMemoriesAsync(archiveDays, deleteDays, context.CancellationToken).ConfigureAwait(false);

        TerminalHelper.WriteLine($"{TerminalColors.Success}{L.T(StringKey.HostMemoryCleanupDone)}{AnsiStyleConstants.Reset}");
        TerminalHelper.WriteLine(L.T(StringKey.HostMemoryChecked, result.CheckedCount));
        TerminalHelper.WriteLine(L.T(StringKey.HostMemoryArchived, result.ArchivedCount));
        TerminalHelper.WriteLine(L.T(StringKey.HostMemoryDeleted, result.DeletedCount));
        TerminalHelper.WriteLine(L.T(StringKey.HostMemoryRetained, result.RetainedCount));
    }
}
