namespace Tools.Handlers;

public partial class FileToolHandlers
{
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

        var validationError = ValidationHelper.ValidateRequired(patch, "patch");
        if (validationError != null)
        {
            var validationDiag = BuildValidationErrorDiagnostic(validationError);
            return ToolResultBuilder.Error().WithText(validationDiag.FormattedMessage).WithDiagnostic(validationDiag).Build();
        }

        var result = await _applyPatchLogic.ApplyAsync(patch, dry_run, workingDirectory: null, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            var errorText = result.ErrorMessage ?? "Patch did not apply";
            if (result.Details.Count > 0)
                errorText += "\n" + string.Join("\n", result.Details);
            var patchDiag = BuildApplyPatchFailedDiagnostic(errorText);
            return ToolResultBuilder.Error().WithText(patchDiag.FormattedMessage).WithDiagnostic(patchDiag).Build();
        }

        var summary = result.DryRun
            ? $"Dry run: {result.FilesWouldModify} file(s) would be modified"
            : $"Applied patch: {result.FilesModified} file(s) modified";
        var detailText = result.Details.Count > 0 ? "\n" + string.Join("\n", result.Details) : "";
        foreach (var modifiedPath in result.ModifiedFilePaths)
        {
            NotifyFileWrite(modifiedPath, "apply-patch");
        }
        return ToolResultBuilder.Success().WithText(summary + detailText).Build();
    }
}
