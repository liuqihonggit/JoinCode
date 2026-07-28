namespace JoinCode.ChatCommands;

/// <summary>
/// /diff 命令 - 交互式 diff 浏览器 — 对齐 TS DiffDialog
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Diff, Description = "View uncommitted changes and per-turn diffs", Usage = "/diff [files|cached]", Category = ChatCommandCategory.Code, ArgumentHint = "[files|cached]")]
public sealed class DiffCommand : ChatCommandBase
{
    public async override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        try
        {
            var processService = ChatCommandBase.GetService<IProcessService>(context)!;
            var subCommand = !string.IsNullOrWhiteSpace(context.Arguments)
                ? ChatCommandBase.GetSplitArgs(context).FirstOrDefault()?.ToLowerInvariant() ?? "default"
                : "default";

            switch (subCommand)
            {
                case DiffModeConstants.Files:
                    await ShowChangedFilesAsync(context.CancellationToken, context.Services.FileSystem, processService).ConfigureAwait(false);
                    break;
                case DiffModeConstants.Cached:
                case DiffModeConstants.Staged:
                    await ShowStagedDiffAsync(context.CancellationToken, context.Services.FileSystem, processService).ConfigureAwait(false);
                    break;
                default:
                    await ShowInteractiveDiffAsync(context.Services.TurnDiffProvider, context.CancellationToken, context.Services.FileSystem, processService).ConfigureAwait(false);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // 取消时正常退出
        }

        return ChatCommandResult.Continue();
    }

    /// <summary>
    /// 交互式 diff 浏览器 — 对齐 TS DiffDialog 主流程
    /// </summary>
    private static async Task ShowInteractiveDiffAsync(ITurnDiffProvider? turnDiffProvider, CancellationToken cancellationToken, IFileSystem fs, IProcessService processService)
    {
        var gitDiffService = new GitDiffService(fs);
        var dialogRenderer = new DiffDialogRenderer();
        var fileListRenderer = new DiffFileListRenderer();
        var turnDiffService = turnDiffProvider as TurnDiffService;

        // 获取 git diff 数据
        var diffData = await gitDiffService.FetchDiffDataAsync(cancellationToken).ConfigureAwait(false);

        // 非交互模式或测试环境回退到文本输出模式
        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive)
        {
            var output = dialogRenderer.Render(new DiffDialogState
            {
                DiffData = diffData,
                ViewMode = DiffViewMode.List,
                SelectedIndex = 0,
                SourceIndex = 0,
                Sources = [new DiffSource.Current()],
                ScrollOffset = 0
            });
            TerminalHelper.WriteLine(output);
            return;
        }

        // 构建数据源列表：Current + Turn Diffs — 对齐 TS DiffDialog
        var sources = new List<DiffSource> { new DiffSource.Current() };
        var turnDiffLookup = new Dictionary<int, TurnDiff>();

        if (turnDiffService is not null)
        {
            var turnDiffs = turnDiffService.GetFullTurnDiffs();
            foreach (var turn in turnDiffs)
            {
                sources.Add(new DiffSource.Turn(turn.TurnIndex, turn.UserPromptPreview));
                turnDiffLookup[turn.TurnIndex] = turn;
            }
        }

        var state = new DiffDialogState
        {
            DiffData = diffData,
            ViewMode = DiffViewMode.List,
            SelectedIndex = 0,
            SourceIndex = 0,
            Sources = sources,
            ScrollOffset = 0
        };

        var escCount = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            // 根据当前源切换 DiffData
            var currentSource = state.Sources.Count > 0 ? state.Sources[state.SourceIndex] : null;
            var currentTurn = currentSource as DiffSource.Turn;
            var currentDiffData = currentTurn is not null && turnDiffLookup.TryGetValue(currentTurn.TurnIndex, out var td)
                ? (turnDiffService ?? throw new InvalidOperationException("TurnDiffService required for turn diff")).TurnDiffToDiffData(td)
                : diffData;

            state = state with { DiffData = currentDiffData };

            // 渲染当前状态
            var output = dialogRenderer.Render(state);
            TerminalHelper.WriteRaw($"{AnsiControlConstants.ClearScreen}{AnsiControlConstants.CursorHome}");
            TerminalHelper.WriteRaw(output);

            // 读取按键（非交互模式检查在方法入口处，此处为 else 分支）
            ConsoleKeyInfo key;
            if (!Core.Utils.TestEnvironmentDetector.IsNonInteractive)
            {
                key = TerminalHelper.ReadKey(true);
            }
            else
            {
                break;
            }

            switch (key.Key)
            {
                case ConsoleKey.Escape:
                    escCount++;
                    if (escCount >= 2 || state.ViewMode == DiffViewMode.List)
                    {
                        // 退出对话框
                        TerminalHelper.WriteRaw($"{AnsiControlConstants.ClearScreen}{AnsiControlConstants.CursorHome}");
                        return;
                    }
                    // Detail 模式下 Esc 返回 List
                    state = state with { ViewMode = DiffViewMode.List };
                    escCount = 0;
                    break;

                case ConsoleKey.Enter:
                    if (state.ViewMode == DiffViewMode.List && state.DiffData.Files.Count > 0)
                    {
                        state = state with { ViewMode = DiffViewMode.Detail };
                    }
                    escCount = 0;
                    break;

                case ConsoleKey.Backspace:
                    if (state.ViewMode == DiffViewMode.Detail)
                    {
                        state = state with { ViewMode = DiffViewMode.List };
                    }
                    escCount = 0;
                    break;

                case ConsoleKey.UpArrow:
                    if (state.ViewMode == DiffViewMode.List)
                    {
                        var newIdx = Math.Max(0, state.SelectedIndex - 1);
                        var newOffset = fileListRenderer.ComputeScrollOffset(newIdx, state.DiffData.Files.Count, state.ScrollOffset);
                        state = state with { SelectedIndex = newIdx, ScrollOffset = newOffset };
                    }
                    escCount = 0;
                    break;

                case ConsoleKey.DownArrow:
                    if (state.ViewMode == DiffViewMode.List)
                    {
                        var newIdx = Math.Min(state.DiffData.Files.Count - 1, state.SelectedIndex + 1);
                        var newOffset = fileListRenderer.ComputeScrollOffset(newIdx, state.DiffData.Files.Count, state.ScrollOffset);
                        state = state with { SelectedIndex = newIdx, ScrollOffset = newOffset };
                    }
                    escCount = 0;
                    break;

                case ConsoleKey.LeftArrow:
                    if (state.ViewMode == DiffViewMode.Detail)
                    {
                        state = state with { ViewMode = DiffViewMode.List };
                    }
                    else if (state.Sources.Count > 1 && state.SourceIndex > 0)
                    {
                        state = state with { SourceIndex = state.SourceIndex - 1, SelectedIndex = 0, ScrollOffset = 0 };
                    }
                    escCount = 0;
                    break;

                case ConsoleKey.RightArrow:
                    if (state.Sources.Count > 1 && state.SourceIndex < state.Sources.Count - 1)
                    {
                        state = state with { SourceIndex = state.SourceIndex + 1, SelectedIndex = 0, ScrollOffset = 0 };
                    }
                    escCount = 0;
                    break;

                default:
                    escCount = 0;
                    break;
            }
        }
    }

    private static async Task ShowStagedDiffAsync(CancellationToken cancellationToken, IFileSystem fs, IProcessService processService)
    {
        TerminalHelper.WriteLine("=== Staged Changes ===\n");

        var result = await RunGitCommandAsync($"{GitSubCommand.Diff.ToValue()} --cached", cancellationToken, fs, processService).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(result))
        {
            TerminalHelper.WriteLine("No staged changes");
        }
        else
        {
            var diffLines = DiffParser.Parse(result);
            var renderer = new DiffViewRenderer();
            renderer.Render(diffLines);
        }
    }

    private static async Task ShowChangedFilesAsync(CancellationToken cancellationToken, IFileSystem fs, IProcessService processService)
    {
        TerminalHelper.WriteLine("=== Changed Files ===\n");

        var modified = await RunGitCommandAsync("diff --name-only", cancellationToken, fs, processService).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(modified))
        {
            TerminalHelper.WriteLine("[Modified but not staged]");
            foreach (var file in modified.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                TerminalHelper.WriteLine($"{TerminalColors.Error}  M {file}{AnsiStyleConstants.Reset}");
            }
        }

        var staged = await RunGitCommandAsync($"{GitSubCommand.Diff.ToValue()} --cached --name-only", cancellationToken, fs, processService).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(staged))
        {
            TerminalHelper.WriteLine("\n[Staged]");
            foreach (var file in staged.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                TerminalHelper.WriteLine($"{TerminalColors.Success}  A {file}{AnsiStyleConstants.Reset}");
            }
        }

        var untracked = await RunGitCommandAsync("ls-files --others --exclude-standard", cancellationToken, fs, processService).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(untracked))
        {
            TerminalHelper.WriteLine("\n[Untracked]");
            foreach (var file in untracked.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                TerminalHelper.WriteLine($"{TerminalColors.Warning}  ? {file}{AnsiStyleConstants.Reset}");
            }
        }

        if (string.IsNullOrWhiteSpace(modified) && string.IsNullOrWhiteSpace(staged) && string.IsNullOrWhiteSpace(untracked))
        {
            TerminalHelper.WriteLine("Working tree is clean");
        }
    }

    private static async Task<string> RunGitCommandAsync(string arguments, CancellationToken cancellationToken, IFileSystem fs, IProcessService processService)
    {
        try
        {
            var options = new ProcessOptions
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = fs.GetCurrentDirectory()
            };

            var result = await processService.ExecuteAsync(options, cancellationToken).ConfigureAwait(false);

            return result.StandardOutput;
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("执行diff命令", ex);
            return string.Empty;
        }
    }
}
