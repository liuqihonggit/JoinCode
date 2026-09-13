namespace Tools.Handlers;

public partial class FileToolHandlers
{
    /// <summary>应用 unified diff patch 到一个或多个文件</summary>
    [McpTool(FileToolNameConstants.FileApplyPatch, "Apply a unified diff patch to one or more files", "file")]
    public async Task<ToolResult> FileApplyPatchAsync(
        [McpToolParameter("The unified diff patch content")] string patch,
        [McpToolParameter("Preview changes without writing (default: false)", Required = false)] bool dry_run = false,
        CancellationToken cancellationToken = default)
    {
        if (_applyPatchLogic is null)
        {
            var notAvailDiag = BuildApplyPatchNotAvailableDiagnostic();
            return ToolResultBuilder.Error().WithText(notAvailDiag.FormattedMessage).WithDiagnostic(notAvailDiag).Build();
        }

        // ── 参数校验 ──
        var validationError = ValidationHelper.ValidateRequired(patch, "patch");
        if (validationError != null)
        {
            var validationDiag = BuildValidationErrorDiagnostic(validationError);
            return ToolResultBuilder.Error().WithText(validationDiag.FormattedMessage).WithDiagnostic(validationDiag).Build();
        }

        // ── 统一写入防御链 — dry_run 不写盘跳过防御，非 dry_run 对每个目标文件跑防御 ──
        if (!dry_run)
        {
            var targetPaths = ExtractPatchTargetPaths(patch);
            if (targetPaths.Count > 0)
            {
                // 对每个目标文件并行跑防御链，任一拒绝即整体拒绝
                var defenses = await Task.WhenAll(
                    targetPaths.Select(async path =>
                    {
                        var safety = await _writeDefense
                            .Begin(path, patch, FileOperationType.Edit, "patching")
                            .Then(_writeDefense.RejectUncPath)           // UNC 路径拒绝
                            .Then(_writeDefense.ResolveSandboxAsync)     // 沙箱路径解析
                            .Then(_writeDefense.CheckTeamMemSecrets)     // 团队密钥检测（patch 内容可能含密钥）
                            .Then(_writeDefense.RequireReadBeforeWrite)  // 写前读校验
                            .Then(_writeDefense.GuardStaleWriteAsync)    // 脏写保护
                            .Then(_writeDefense.BackupBeforeWriteAsync)  // 写前备份
                            .ExecuteAsync(cancellationToken).ConfigureAwait(false);
                        return (Path: path, Safety: safety);
                    })).ConfigureAwait(false);

                var rejectedDefense = defenses.FirstOrDefault(d => d.Safety.Rejection is not null);
                if (rejectedDefense.Safety.Rejection is not null)
                    return rejectedDefense.Safety.Rejection;
            }
        }

        // ── 应用 patch ──
        var result = await _applyPatchLogic.ApplyAsync(patch, dry_run, workingDirectory: null, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            var errorText = result.ErrorMessage ?? "Patch did not apply";
            if (result.Details.Count > 0)
                errorText += "\n" + string.Join("\n", result.Details);
            var patchDiag = BuildApplyPatchFailedDiagnostic(errorText);
            return ToolResultBuilder.Error().WithText(patchDiag.FormattedMessage).WithDiagnostic(patchDiag).Build();
        }

        // ── 构建成功响应 ──
        var summary = result.DryRun
            ? $"Dry run: {result.FilesWouldModify} file(s) would be modified"
            : $"Applied patch: {result.FilesModified} file(s) modified";
        var detailText = result.Details.Count > 0 ? "\n" + string.Join("\n", result.Details) : "";

        // ── 统一写入后通知（每个修改的文件） ──
        foreach (var modifiedPath in result.ModifiedFilePaths)
        {
            _writeDefense.NotifyWriteComplete(modifiedPath, null, "apply-patch", FileOperationType.Edit);
        }

        return ToolResultBuilder.Success().WithText(summary + detailText).Build();
    }

    /// <summary>
    /// 从 unified diff patch 中提取目标文件路径（+++ b/path 行，去掉 b/ 前缀）。
    /// 对齐 ApplyPatchLogic.ParsePatch 的路径提取逻辑（L121-128）。
    /// 用于在应用 patch 前对每个目标文件跑写入防御链。
    /// </summary>
    private static List<string> ExtractPatchTargetPaths(string patch)
    {
        var paths = new List<string>();
        foreach (var line in patch.AsSpan().EnumerateLines())
        {
            if (!line.StartsWith("+++".AsSpan())) continue;
            if (line.Length < 4) continue;
            var path = line.Slice(4);
            if (path.StartsWith("b/".AsSpan()))
                path = path.Slice(2);
            paths.Add(path.Trim().ToString());
        }
        return paths;
    }
}
