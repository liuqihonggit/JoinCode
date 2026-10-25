namespace NativeTui.Tests.Repl;

/// <summary>
/// S11 统一接线 — 命令面冒烟（S10 接线点 12/13/14）：
/// 斜杠命令使用记账（RecordUse → recently-used 分组投影）、`!` 前缀 bash 直通
///（本地执行不进引擎 + 结果 ConcurrentQueue 缓冲主循环上屏 + 退出码分级样式）、
/// `@` 文件补全（token 提取 → SuggestionBox 投影 → Tab 接受替换写回）。
/// </summary>
public sealed class ReplShellWiringCommandUxTests
{
    /// <summary>非拦截参数命令载体（/theme 被 S9 本地拦截不可用作透传载体）。</summary>
    private static readonly IReadOnlyList<CommandSuggestion> Commands =
    [
        new("help", "显示帮助", [], IsHidden: false),
        new("compact", "压缩上下文", [], IsHidden: false),
        new("effort", "推理力度", [], IsHidden: false, Arguments: ["low", "medium", "high"]),
    ];

    /// <summary>构造测试 shell（可选工作区根注入 @ 补全）。</summary>
    private static (ReplShell Shell, FakeReplEngine Engine, CommandUsageMemory Usage) CreateShell(string? workspaceRoot = null)
    {
        var engine = new FakeReplEngine();
        var banner = new BannerItem("1.0.0", "test-model", "API 按量计费", "/tmp");
        var usage = new CommandUsageMemory(timeProvider: new FakeTimeProvider());
        var shell = new ReplShell(engine, banner, new StringWriter(), commands: Commands,
            sizeSource: new FakeTerminalSizeSource(80, 24), commandUsage: usage, workspaceRoot: workspaceRoot);
        shell.SeedContent();
        return (shell, engine, usage);
    }

    /// <summary>模拟键入（经 HandleKey 全链 — 触发建议刷新链）。</summary>
    private static void Type(ReplShell shell, string text)
    {
        foreach (var c in text)
        {
            shell.HandleKey(new ConsoleKeyInfo(c, ConsoleKey.None, false, false, false));
        }
    }

    /// <summary>接线点12 — 斜杠命令提交记账：RecordUse 计数 +1（未知名不记账防污染分组）。</summary>
    [Fact]
    public void SlashSubmit_RecordsCommandUse()
    {
        var (shell, engine, usage) = CreateShell();

        Type(shell, "/help");
        shell.Submit();
        Type(shell, "/help");
        shell.Submit();
        Type(shell, "/nosuch");
        shell.Submit();

        Assert.Equal(2, usage.TryGetUsage("help")?.Count);
        Assert.Null(usage.TryGetUsage("nosuch"));
        Assert.Equal(["/help", "/help", "/nosuch"], engine.Submits);
    }

    /// <summary>接线点12 — recently-used 分组投影：使用过的命令在空查询 "/" 时升至首组。</summary>
    [Fact]
    public void SlashEmptyQuery_RecentUsedFirst()
    {
        var (shell, _, _) = CreateShell();

        Type(shell, "/compact");
        shell.Submit();
        Type(shell, "/");

        var names = shell.Suggester!.Items.Select(item => item.Name).ToList();
        // recently-used 组在来源组之前：compact 首位，其余字母序
        Assert.Equal("compact", names[0]);
        Assert.Equal(["effort", "help"], names.Skip(1).Take(2).OrderBy(static n => n).ToList());
    }

    /// <summary>接线点12（语义补全）— 通用参数模式 Enter 接受并提交（非拦截命令载体）。</summary>
    [Fact]
    public void ArgumentEnter_NonInterceptedCommand_Submits()
    {
        var (shell, engine, _) = CreateShell();

        Type(shell, "/effort m");
        shell.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        Assert.Equal(["/effort medium"], engine.Submits);
        Assert.Equal(string.Empty, shell.Input.Text);
    }

    /// <summary>接线点13 — `!` 前缀 bash 直通：本地执行不进引擎，成功结果 AssistantMessageItem 上屏
    ///（echo 跨 shell 通用；DrainEngine 消费并发队列）。</summary>
    [Fact]
    public async Task BashDirect_EchoCommand_ResultsAsAssistantMessage()
    {
        var (shell, engine, _) = CreateShell();

        Type(shell, "!echo wiring-smoke");
        shell.Submit();

        // fire-and-forget 执行完成入队（echo 秒级完成；轮询 DrainEngine 消费上屏）
        await WaitUntilAsync(() =>
        {
            shell.DrainEngine();
            return shell.View.Messages.Items.Any(m => m is AssistantMessageItem);
        });

        Assert.Empty(engine.Submits);
        Assert.Contains(shell.View.Messages.Items, m => m is UserMessageItem { Text: "!echo wiring-smoke" });
        var assistant = shell.View.Messages.Items.OfType<AssistantMessageItem>().Single();
        Assert.Contains("wiring-smoke", assistant.Text);
    }

    /// <summary>接线点13 — `!` 退出码非 0：error 样式上屏（ErrorMessageItem + 退出码提示）。</summary>
    [Fact]
    public async Task BashDirect_NonZeroExit_ResultsAsErrorMessage()
    {
        var (shell, engine, _) = CreateShell();

        Type(shell, "!exit 1");
        shell.Submit();

        await WaitUntilAsync(() =>
        {
            shell.DrainEngine();
            return shell.View.Messages.Items.Any(m => m is ErrorMessageItem);
        });

        Assert.Empty(engine.Submits);
        var error = shell.View.Messages.Items.OfType<ErrorMessageItem>().Single();
        Assert.Contains("退出码 1", error.Text);
    }

    /// <summary>接线点13 — 裸 `!` 空输入进入 Bash 模式（TASK074 D1b — 对齐基准 PromptInput.tsx:869-887
    /// 空输入+! → onModeChange('bash') + 剥前缀进入态；不插入 ! 字符不透传引擎）。</summary>
    [Fact]
    public void BareExclamation_EntersBashMode()
    {
        var (shell, _, _) = CreateShell();

        Type(shell, "!");

        Assert.Equal(InputMode.Bash, shell.InputMode);
        Assert.Equal("", shell.Input.Text);
    }

    /// <summary>接线点14 — @token 输入触发文件补全投影（相对路径名 + 选中索引 0 预选）。</summary>
    [Fact]
    public void AtToken_SuggestsWorkspaceFiles()
    {
        using var workspace = TempDirectoryScope.Create();
        File.WriteAllText(Path.Combine(workspace.Path, "wiring-a.txt"), "a");
        File.WriteAllText(Path.Combine(workspace.Path, "wiring-b.txt"), "b");
        File.WriteAllText(Path.Combine(workspace.Path, "other.txt"), "x");
        var (shell, _, _) = CreateShell(workspaceRoot: workspace.Path);

        Type(shell, "看 @wiring");

        var suggestions = shell.AtSuggestions;
        Assert.NotNull(suggestions);
        Assert.Equal(["wiring-a.txt", "wiring-b.txt"], suggestions!.Select(s => s.Name).ToArray());
        Assert.Equal(0, shell.AtSuggestionIndex);
    }

    /// <summary>接线点14 — Tab 接受替换写回：@token 替换为 @+选中相对路径，光标置替换段末尾。</summary>
    [Fact]
    public void AtSuggestion_Tab_ReplacesTokenWithSelectedPath()
    {
        using var workspace = TempDirectoryScope.Create();
        File.WriteAllText(Path.Combine(workspace.Path, "wiring-a.txt"), "a");
        File.WriteAllText(Path.Combine(workspace.Path, "wiring-b.txt"), "b");
        var (shell, _, _) = CreateShell(workspaceRoot: workspace.Path);

        Type(shell, "看 @wiring");
        shell.HandleKey(new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false));

        Assert.Equal("看 @wiring-a.txt", shell.Input.Text);
        Assert.Equal(shell.Input.Text.Length, shell.Input.CursorCol);
        // 接受后建议关闭
        Assert.Null(shell.AtSuggestions);
    }

    /// <summary>接线点14 — Esc 关闭 @ 建议 + 同 token 抑制（编辑变化解除抑制重新建议）。</summary>
    [Fact]
    public void AtSuggestion_Escape_SuppressesSameToken()
    {
        using var workspace = TempDirectoryScope.Create();
        File.WriteAllText(Path.Combine(workspace.Path, "wiring-a.txt"), "a");
        var (shell, _, _) = CreateShell(workspaceRoot: workspace.Path);

        Type(shell, "@wiring");
        Assert.NotNull(shell.AtSuggestions);
        shell.HandleKey(new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false));
        Assert.Null(shell.AtSuggestions);

        // 同 token 重新编辑刷新 → 抑制（不重新建议）
        Type(shell, "g");
        Assert.Null(shell.AtSuggestions);
    }

    /// <summary>接线点14 — 斜杠命令态优先：/ 输入不触发 @ 建议（互斥）。</summary>
    [Fact]
    public void SlashInput_DoesNotTriggerAtSuggestions()
    {
        var (shell, _, _) = CreateShell(workspaceRoot: "/tmp");

        Type(shell, "/help");

        Assert.True(shell.Suggester!.IsActive);
        Assert.Null(shell.AtSuggestions);
    }

    /// <summary>轮询等待条件成立（bash 直通异步完成缓冲上屏；5s 超时断言失败）。</summary>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            try
            {
                await Task.Delay(20, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

/// <summary>临时目录作用域（@ 补全测试工作区 — using 退出清理）。</summary>
internal sealed class TempDirectoryScope : IDisposable
{
    /// <summary>临时目录路径。</summary>
    public string Path { get; }

    private TempDirectoryScope(string path) => Path = path;

    /// <summary>创建临时目录（进程退出兜底清理由系统临时目录语义承担）。</summary>
    public static TempDirectoryScope Create()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tui-wiring-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return new TempDirectoryScope(path);
    }

    /// <summary>清理临时目录（best-effort — 测试产物不适用禁删红线的交互式约束）。</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>固定时间提供者（CommandUsageMemory 时间注入 — 计数断言确定性）。</summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now = DateTimeOffset.UnixEpoch;

    /// <summary>当前时间（固定 — 计数与时间戳确定）。</summary>
    public override DateTimeOffset GetUtcNow() => _now;
}
