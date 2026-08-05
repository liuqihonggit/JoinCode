namespace Tools.Handlers;

/// <summary>
/// 错误修复工具处理器 — ToolKind.OnError，仅在工具执行失败时动态注入
/// 不出现在首次系统提示词中，仅在 OnErrorToolInjectionMiddleware 检测到错误后注入
/// GroupName 匹配失败工具名，实现精准修复推荐
/// </summary>
[McpToolDispatch(ToolCategory.ErrorRecovery, Kind = ToolKind.OnError)]
public class ErrorRecoveryToolHandlers
{
    private readonly IFileSystem _fs;
    private readonly IGitCommandRunner _gitRunner;
    private readonly ILogger<ErrorRecoveryToolHandlers>? _logger;

    public ErrorRecoveryToolHandlers(IFileSystem fs, IGitCommandRunner gitRunner, ILogger<ErrorRecoveryToolHandlers>? logger = null)
    {
        _fs = fs;
        _gitRunner = gitRunner;
        _logger = logger;
    }

    /// <summary>
    /// 诊断工具 — 分析工具执行失败原因，提供修复建议
    /// GroupName="diagnostic" 表示当任何工具失败时都可推荐此工具
    /// </summary>
    [McpTool("diagnose_error", "分析工具执行失败的错误信息，提供诊断和修复建议", "error_recovery",
        Kind = JoinCode.Abstractions.Attributes.ToolKindConstants.OnError, GroupName = "diagnostic")]
    public async Task<ToolResult> DiagnoseErrorAsync(
        [McpToolParameter("失败的错误信息", Required = true)] string errorMessage,
        [McpToolParameter("失败的工具名称", Required = true)] string failedToolName,
        [McpToolParameter("工作目录路径", Required = false)] string? workingDirectory,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder(512);
        sb.AppendLine($"## 工具 '{failedToolName}' 执行失败诊断");
        sb.AppendLine();
        sb.AppendLine($"**错误信息**: {errorMessage}");
        sb.AppendLine();

        sb.AppendLine("### 常见原因分析");
        var category = ErrorClassifier.Classify(errorMessage);
        switch (category)
        {
            case ToolErrorCategory.Permission:
            case ToolErrorCategory.AccessDenied:
                sb.AppendLine("- **权限不足**: 当前用户可能没有操作目标文件/目录的权限");
                sb.AppendLine("- 建议: 检查文件权限，或使用 `chmod`/`icacls` 修改权限");
                break;
            case ToolErrorCategory.NotFound:
                sb.AppendLine("- **路径不存在**: 目标文件或目录可能不存在");
                sb.AppendLine("- 建议: 先使用 `directory_list` 确认路径，再执行操作");
                break;
            case ToolErrorCategory.Timeout:
                sb.AppendLine("- **执行超时**: 命令执行时间过长");
                sb.AppendLine("- 建议: 拆分任务为更小的步骤，或增加超时时间");
                break;
            default:
                sb.AppendLine("- 通用建议: 检查输入参数是否正确，确认目标资源是否可用");
                break;
        }

        if (!string.IsNullOrEmpty(workingDirectory) && _fs.DirectoryExists(workingDirectory))
        {
            sb.AppendLine();
            sb.AppendLine($"### 工作目录状态: {workingDirectory}");
            try
            {
                var entries = _fs.GetFiles(workingDirectory, "*", SearchOption.TopDirectoryOnly);
                sb.AppendLine($"- 文件数: {entries.Length}");
            }
            catch
            {
                sb.AppendLine("- 无法读取工作目录");
            }
        }

        return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
    }

    /// <summary>
    /// Shell 错误修复工具 — 专门处理 Shell 命令执行失败
    /// GroupName="Bash" 表示当 Bash 工具失败时精准推荐此工具
    /// </summary>
    [McpTool("fix_shell_error", "分析 Shell 命令执行失败原因，提供替代命令建议", "error_recovery",
        Kind = JoinCode.Abstractions.Attributes.ToolKindConstants.OnError, GroupName = "Bash")]
    public Task<ToolResult> FixShellErrorAsync(
        [McpToolParameter("失败的命令", Required = true)] string failedCommand,
        [McpToolParameter("错误输出", Required = true)] string errorOutput,
        [McpToolParameter("退出码", Required = false)] int exitCode,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder(512);
        sb.AppendLine("## Shell 命令失败修复建议");
        sb.AppendLine();
        sb.AppendLine($"**失败命令**: `{failedCommand}`");
        sb.AppendLine($"**退出码**: {exitCode}");
        sb.AppendLine($"**错误输出**: {errorOutput}");
        sb.AppendLine();

        sb.AppendLine("### 替代方案");
        var shellCategory = ErrorClassifier.Classify(errorOutput);
        switch (shellCategory)
        {
            case ToolErrorCategory.CommandNotFound:
                sb.AppendLine("- 命令不存在，可能需要安装对应工具包");
                sb.AppendLine("- 使用 `where`/`which` 检查命令是否在 PATH 中");
                sb.AppendLine("- 考虑使用 PowerShell 等效命令替代");
                break;
            case ToolErrorCategory.Permission:
            case ToolErrorCategory.AccessDenied:
                sb.AppendLine("- 权限不足，尝试以管理员身份运行");
                sb.AppendLine("- 检查文件/目录权限设置");
                break;
            default:
                sb.AppendLine("- 检查命令语法是否正确");
                sb.AppendLine("- 确认所有参数和路径是否有效");
                sb.AppendLine("- 尝试分步执行复杂命令");
                break;
        }

        return Task.FromResult(ToolResultBuilder.Success().WithText(sb.ToString()).Build());
    }

    /// <summary>
    /// 文件操作错误修复工具 — 专门处理文件读写失败
    /// GroupName="Read" 表示当 Read 工具失败时精准推荐此工具
    /// </summary>
    [McpTool("fix_file_error", "分析文件操作失败原因，提供修复路径建议", "error_recovery",
        Kind = JoinCode.Abstractions.Attributes.ToolKindConstants.OnError, GroupName = "Read")]
    public Task<ToolResult> FixFileErrorAsync(
        [McpToolParameter("失败的文件路径", Required = true)] string filePath,
        [McpToolParameter("错误信息", Required = true)] string errorMessage,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder(512);
        sb.AppendLine("## 文件操作失败修复建议");
        sb.AppendLine();
        sb.AppendLine($"**文件路径**: `{filePath}`");
        sb.AppendLine($"**错误信息**: {errorMessage}");
        sb.AppendLine();

        var dir = Path.GetDirectoryName(filePath);
        var fileName = Path.GetFileName(filePath);

        if (!_fs.FileExists(filePath) && !_fs.DirectoryExists(filePath))
        {
            sb.AppendLine("### 文件不存在");
            if (!string.IsNullOrEmpty(dir) && _fs.DirectoryExists(dir))
            {
                sb.AppendLine($"目录 `{dir}` 存在，但文件 `{fileName}` 不存在");
                try
                {
                    var similar = _fs.GetFiles(dir, "*", SearchOption.TopDirectoryOnly)
                        .Where(f => Path.GetFileName(f).Contains(Path.GetFileNameWithoutExtension(fileName), StringComparison.OrdinalIgnoreCase))
                        .Take(5).ToArray();
                    if (similar.Length > 0)
                    {
                        sb.AppendLine("可能的目标文件:");
                        foreach (var f in similar)
                            sb.AppendLine($"- {f}");
                    }
                }
                catch (Exception)
                {
                    _logger?.LogDebug("无法列出目录 {Dir} 中的相似文件", dir);
                }
            }
            else if (!string.IsNullOrEmpty(dir))
            {
                sb.AppendLine($"目录 `{dir}` 也不存在，可能路径有误");
            }
        }
        else
        {
            sb.AppendLine("### 文件存在但操作失败");
            sb.AppendLine("- 可能是权限问题或文件被占用");
            sb.AppendLine("- 建议检查文件是否被其他进程锁定");
        }

        return Task.FromResult(ToolResultBuilder.Success().WithText(sb.ToString()).Build());
    }

    /// <summary>
    /// 合并冲突修复工具 — 专门处理 worktree merge 失败
    /// GroupName="worktree_merge" 表示当 worktree_merge 工具失败时精准推荐此工具
    /// </summary>
    [McpTool("fix_merge_conflict", "分析 worktree 合并失败原因，检测遗留冲突标记和分支冲突，提供修复建议", "error_recovery",
        Kind = JoinCode.Abstractions.Attributes.ToolKindConstants.OnError, GroupName = "worktree_merge")]
    public async Task<ToolResult> FixMergeConflictAsync(
        [McpToolParameter("源 worktree 路径", Required = true)] string source_worktree_path,
        [McpToolParameter("目标 worktree 路径", Required = true)] string target_worktree_path,
        [McpToolParameter("错误信息", Required = true)] string error_message,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder(512);
        sb.AppendLine("## Worktree 合并冲突修复建议");
        sb.AppendLine();
        sb.AppendLine($"**源 worktree**: `{source_worktree_path}`");
        sb.AppendLine($"**目标 worktree**: `{target_worktree_path}`");
        sb.AppendLine($"**错误信息**: {error_message}");
        sb.AppendLine();

        var hasStaleMarkers = false;
        var hasBranchConflict = false;

        sb.AppendLine("### 诊断结果");

        try
        {
            var sourceStale = await _gitRunner.DetectStaleConflictMarkersAsync(source_worktree_path, ct).ConfigureAwait(false);
            if (sourceStale.HasStaleMarkers)
            {
                hasStaleMarkers = true;
                sb.AppendLine($"- ⚠️ **源 worktree 存在遗留冲突标记**: {string.Join(", ", sourceStale.Files)}");
                sb.AppendLine("  这些文件包含未清理的 `<<<<<<<`/`=======`/`>>>>>>>` 标记");
                sb.AppendLine("  需要手动解决冲突后重新 commit，或用 `git checkout -- <file>` 撤回");
            }

            var targetStale = await _gitRunner.DetectStaleConflictMarkersAsync(target_worktree_path, ct).ConfigureAwait(false);
            if (targetStale.HasStaleMarkers)
            {
                hasStaleMarkers = true;
                sb.AppendLine($"- ⚠️ **目标 worktree 存在遗留冲突标记**: {string.Join(", ", targetStale.Files)}");
                sb.AppendLine("  这些文件包含未清理的 `<<<<<<<`/`=======`/`>>>>>>>` 标记");
            }

            if (!hasStaleMarkers)
            {
                sb.AppendLine("- ✅ 两边 worktree 均无遗留冲突标记");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "检测遗留冲突标记失败");
            sb.AppendLine("- 无法检测遗留冲突标记");
        }

        try
        {
            var sourceBranchResult = await _gitRunner.ExecuteAsync("branch --show-current", source_worktree_path, ct).ConfigureAwait(false);
            var targetBranchResult = await _gitRunner.ExecuteAsync("branch --show-current", target_worktree_path, ct).ConfigureAwait(false);

            if (sourceBranchResult.Success && targetBranchResult.Success)
            {
                var sourceBranch = sourceBranchResult.Output.Trim();
                var targetBranch = targetBranchResult.Output.Trim();

                if (!string.IsNullOrEmpty(sourceBranch) && !string.IsNullOrEmpty(targetBranch))
                {
                    var conflictCheck = await _gitRunner.DetectMergeConflictAsync(targetBranch, sourceBranch, target_worktree_path, ct).ConfigureAwait(false);
                    if (conflictCheck.HasConflict)
                    {
                        hasBranchConflict = true;
                        sb.AppendLine($"- ⚠️ **分支合并冲突**: {targetBranch} ← {sourceBranch}");
                        sb.AppendLine($"  冲突文件: {string.Join(", ", conflictCheck.ConflictFiles)}");
                    }
                    else
                    {
                        sb.AppendLine($"- ✅ 分支 {targetBranch} 与 {sourceBranch} 无合并冲突");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "检测分支合并冲突失败");
            sb.AppendLine("- 无法检测分支合并冲突");
        }

        sb.AppendLine();
        sb.AppendLine("### 修复方案");
        if (hasStaleMarkers)
        {
            sb.AppendLine("1. **清理遗留冲突标记**:");
            sb.AppendLine("   - 检查冲突文件，手动解决冲突内容");
            sb.AppendLine("   - `git add <file>` 标记已解决");
            sb.AppendLine("   - `git commit` 完成解决");
            sb.AppendLine("   - 或 `git merge --abort` 撤回整个 merge");
        }
        if (hasBranchConflict)
        {
            sb.AppendLine("2. **解决分支冲突**:");
            sb.AppendLine("   - 使用 `worktree_merge` 工具时指定 strategy=ours 或 strategy=theirs");
            sb.AppendLine("   - 或手动在目标 worktree 中解决冲突后 commit");
        }
        if (!hasStaleMarkers && !hasBranchConflict)
        {
            sb.AppendLine("- 冲突可能已在预检后自动解决，尝试重新执行 merge");
        }

        return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
    }
}
