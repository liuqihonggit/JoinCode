
namespace JoinCode.Abstractions.Prompts.ToolPrompts;

/// <summary>
/// PowerShellTool 提示词
/// </summary>
[ToolPrompt(ToolName = "PowerShell", Category = ToolPromptCategory.Shell)]
public static class PowerShellToolPrompt {
    public const string ToolName = ShellToolNameConstants.Powershell;

    #region Edition Section Templates

    private const string DesktopEditionSection = @"PowerShell 版本: Windows PowerShell 5.1 (powershell.exe)
   - 管道链操作符 `&&` 和 `||` 不可用 — 它们会导致解析器错误。仅在 A 成功时运行 B：`A; if ($?) { B }`。无条件链式：`A; B`。
   - 三元运算符 (`?:`)、空合并运算符 (`??`) 和空条件运算符 (`?.`) 不可用。请改用 `if/else` 和显式的 `$null -eq` 检查。
   - 避免在原生可执行文件上使用 `2>&1`。在 5.1 中，在 PowerShell 内部重定向原生命令的 stderr 会将每行包装在 ErrorRecord (NativeCommandError) 中，即使 exe 返回退出代码 0，也会将 `$?` 设置为 `$false`。stderr 已经为你捕获 — 不要重定向它。
   - 默认文件编码是 UTF-16 LE（带 BOM）。当写入其他工具将读取的文件时，向 `Out-File`/`Set-Content` 传递 `-Encoding utf8`。
   - `ConvertFrom-Json` 返回 PSCustomObject，而不是哈希表。`-AsHashtable` 不可用。";

    private const string CoreEditionSection = @"PowerShell 版本: PowerShell 7+ (pwsh)
   - 管道链操作符 `&&` 和 `||` 可用，工作方式类似 bash。当 cmd2 仅在 cmd1 成功时才应运行时，优先使用 `cmd1 && cmd2` 而不是 `cmd1; cmd2`。
   - 三元运算符 (`$cond ? $a : $b`)、空合并运算符 (`??`) 和空条件运算符 (`?.`) 可用。
   - 默认文件编码是无 BOM 的 UTF-8。";

    private const string UnknownEditionSection = @"PowerShell 版本: 未知 — 为兼容性假设为 Windows PowerShell 5.1
   - 不要使用 `&&`、`||`、三元运算符 `?:`、空合并运算符 `??` 或空条件运算符 `?.`。这些仅 PowerShell 7+ 支持，在 5.1 上会导致解析器错误。
   - 要条件链式命令：`A; if ($?) { B }`。无条件：`A; B`。";

    #endregion

    #region Background Task Templates

    private const string BackgroundNoteEnabled = @"  - 你可以使用 `run_in_background` 参数在后台运行命令。仅当你不需要立即获得结果，并且可以等待命令稍后完成时收到通知时才使用此功能。你不需要立即检查输出 - 命令完成时你会收到通知。";

    private const string SleepGuidanceEnabled = @"  - 避免不必要的 `Start-Sleep` 命令：
    - 不要在可以立即运行的命令之间休眠 — 直接运行它们。
    - 如果你的命令运行时间较长，并且你希望在完成时收到通知 — 只需使用 `run_in_background` 运行你的命令。在这种情况下不需要休眠。
    - 不要在休眠循环中重试失败的命令 — 诊断根本原因或考虑替代方法。
    - 如果等待你使用 `run_in_background` 启动的后台任务，它完成时你会收到通知 — 不要轮询。
    - 如果你必须轮询外部进程，请使用检查命令而不是先休眠。
    - 如果你必须休眠，请保持持续时间较短（1-5 秒）以避免阻塞用户。";

    #endregion

    #region Main Prompt Template

    /// <summary>
    /// 主提示词模板 - 使用字符串拼接避免硬编码替换键
    /// </summary>
    private static string BuildMainPromptTemplate(
        string editionSection,
        string backgroundNote,
        string sleepGuidance,
        int maxTimeoutMs,
        int defaultTimeoutMs,
        int maxOutputLength)
    {
        var maxTimeoutMinutes = maxTimeoutMs / 60000;
        var defaultTimeoutMinutes = defaultTimeoutMs / 60000;

        return string.Concat(
            "执行给定的 PowerShell 命令，可选超时。工作目录在命令之间持久化；shell 状态（变量、函数）不会。\n\n",
            "重要：此工具用于通过 PowerShell 进行终端操作：git、npm、docker 和 PS cmdlet。不要将其用于文件操作（读取、写入、编辑、搜索、查找文件）- 请改用专门的工具。\n\n",
            editionSection,
            "\n\n在执行命令之前，请遵循以下步骤：\n\n",
            "1. 目录验证：\n",
            "   - 如果命令将创建新目录或文件，首先使用 `Get-ChildItem`（或 `ls`）验证父目录存在且是正确的位置\n\n",
            "2. 命令执行：\n",
            "   - 始终用双引号引用包含空格的文件路径\n",
            "   - 捕获命令的输出。\n\n",
            "PowerShell 语法说明：\n",
            "   - 变量使用 $ 前缀：$myVar = \"value\"\n",
            "   - 转义字符是反引号 (`)，而不是反斜杠\n",
            "   - 使用动词-名词 cmdlet 命名：Get-ChildItem、Set-Location、New-Item、Remove-Item\n",
            "   - 常见别名：ls (Get-ChildItem)、cd (Set-Location)、cat (Get-Content)、rm (Remove-Item)\n",
            "   - 管道运算符 | 工作方式类似 bash，但传递对象而不是文本\n",
            "   - 使用 Select-Object、Where-Object、ForEach-Object 进行筛选和转换\n",
            "   - 字符串插值：\"Hello $name\" 或 \"Hello $($obj.Property)\"\n",
            "   - 注册表访问使用 PSDrive 前缀：`HKLM:\\SOFTWARE\\...`、`HKCU:\\...` — 不是原始的 `HKEY_LOCAL_MACHINE\\...`\n",
            "   - 环境变量：使用 `$env:NAME` 读取，使用 `$env:NAME = \"value\"` 设置（不是 `Set-Variable` 或 bash `export`）\n",
            "   - 通过调用运算符调用路径中包含空格的原生 exe：`& \"C:\\Program Files\\App\\app.exe\" arg1 arg2`\n\n",
            "交互式和阻塞命令（将挂起 — 此工具以 -NonInteractive 运行）：\n",
            "   - 永远不要使用 `Read-Host`、`Get-Credential`、`Out-GridView`、`$Host.UI.PromptForChoice` 或 `pause`\n",
            "   - 破坏性 cmdlet（`Remove-Item`、`Stop-Process`、`Clear-Content` 等）可能会提示确认。当你打算继续操作时，添加 `-Confirm:$false`。对只读/隐藏项使用 `-Force`。\n",
            "   - 永远不要使用 `git rebase -i`、`git add -i` 或其他打开交互式编辑器的命令\n\n",
            "向原生可执行文件传递多行字符串（提交消息、文件内容）：\n",
            "   - 使用单引号的 here-string，这样 PowerShell 不会展开其中的 `$` 或反引号。关闭的 `'@` 必须位于第 0 列（无前导空格）且单独成行 — 缩进它是一个解析错误：\n",
            "<example>\n",
            "git commit -m @'\n",
            "Commit message here.\n",
            "Second line with $literal dollar signs.\n",
            "'@\n",
            "</example>\n",
            "   - 使用 `@'...'@`（单引号，字面量）而不是 `@\"...\"@`（双引号，插值），除非你需要变量展开\n",
            "   - 对于包含 `-`、`@` 或其他 PowerShell 解析为操作符的字符的参数，使用停止解析标记：`git log --% --format=%H`\n\n",
            "使用说明：\n",
            "  - 命令参数是必需的。\n",
            $"  - 你可以指定可选的超时时间（以毫秒为单位，最多 {maxTimeoutMs}ms / {maxTimeoutMinutes} 分钟）。如果未指定，命令将在 {defaultTimeoutMs}ms（{defaultTimeoutMinutes} 分钟）后超时。\n",
            "  - 如果你写清楚、简洁的命令描述，会非常有帮助。\n",
            $"  - 如果输出超过 {maxOutputLength} 个字符，输出将在返回给你之前被截断。\n",
            backgroundNote,
            "\n  - 避免使用 PowerShell 运行有专门工具的命令，除非明确指示：\n",
            "    - 文件搜索：使用 Glob（不是 Get-ChildItem -Recurse）\n",
            "    - 内容搜索：使用 Grep（不是 Select-String）\n",
            "    - 读取文件：使用 FileRead（不是 Get-Content）\n",
            "    - 编辑文件：使用 FileEdit\n",
            "    - 写入文件：使用 FileWrite（不是 Set-Content/Out-File）\n",
            "    - 通信：直接输出文本（不是 Write-Output/Write-Host）\n",
            "  - 当发出多个命令时：\n",
            "    - 如果命令是独立的且可以并行运行，在单条消息中进行多个 PowerShell 工具调用。\n",
            "    - 如果命令相互依赖且必须顺序运行，在单个 PowerShell 调用中链式执行它们（参见上面版本特定的链式语法）。\n",
            "    - 仅当你需要顺序运行命令但不关心前面的命令是否失败时使用 `;`。\n",
            "    - 不要使用换行符分隔命令（换行符在引号字符串和 here-string 中是可以的）\n",
            "  - 不要用 `cd` 或 `Set-Location` 前缀命令 -- 工作目录已经自动设置为正确的项目目录。\n",
            sleepGuidance,
            "\n  - 对于 git 命令：\n",
            "    - 优先创建新提交而不是修改现有提交。\n",
            "    - 在运行破坏性操作（例如 git reset --hard、git push --force、git checkout --）之前，考虑是否有更安全的替代方案可以达到相同的目标。仅在它们确实是最佳方法时才使用破坏性操作。\n",
            "    - 永远不要跳过钩子（--no-verify）或绕过签名（--no-gpg-sign、-c commit.gpgsign=false），除非用户明确要求。如果钩子失败，调查并修复根本问题。\n",
            "    - **重要：禁用分页器** - `git log`、`git diff` 等命令默认会启动交互式分页器（如 less），导致终端卡住等待输入。在非交互式环境中必须添加 `--no-pager` 参数：\n",
            "      - 正确：`git --no-pager log --oneline -10`\n",
            "      - 正确：`git --no-pager diff HEAD~1`\n",
            "      - 错误：`git log --oneline -10`（会卡住等待按 q 退出）\n"
        );
    }

    #endregion

    /// <summary>
    /// 获取 PowerShell 版本特定语法指导
    /// </summary>
    private static string GetEditionSection(string? edition) {
        return edition?.ToLower() switch {
            "desktop" => DesktopEditionSection,
            "core" => CoreEditionSection,
            _ => UnknownEditionSection
        };
    }

    /// <summary>
    /// 无参版本 — 供 ToolPromptGenerator 源码生成器识别并注册
    /// 默认使用 unknown edition（保守兼容语法），运行时通过 ShellInfoSection 注入版本信息
    /// </summary>
    public static string GetDescription() => GetPrompt(edition: null);

    /// <summary>
    /// 获取工具提示词
    /// </summary>
    public static string GetPrompt(
        string? edition = null,
        bool backgroundTasksEnabled = true,
        int defaultTimeoutMs = WorkflowConstants.ToolExecution.PowerShellDefaultTimeoutMs,
        int maxTimeoutMs = 600000,
        int maxOutputLength = WorkflowConstants.ToolExecution.MaxOutputLength)
    {
        var backgroundNote = backgroundTasksEnabled
            ? BackgroundNoteEnabled
            : string.Empty;

        var sleepGuidance = backgroundTasksEnabled
            ? SleepGuidanceEnabled
            : string.Empty;

        var editionSection = GetEditionSection(edition);

        return BuildMainPromptTemplate(
            editionSection,
            backgroundNote,
            sleepGuidance,
            maxTimeoutMs,
            defaultTimeoutMs,
            maxOutputLength);
    }
}
