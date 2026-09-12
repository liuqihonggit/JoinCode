namespace Services.StepEvidence.ToolHandlers;

/// <summary>
/// 步骤完成证据驱动工具 — 对齐 Reasonix complete_step
/// 强制模型在标记步骤完成时必须提供证据，无证据的完成被拒绝。
/// 与 TodoWrite 互补：TodoWrite 管理任务列表状态，complete_step 是步骤的正式签收。
/// </summary>
[McpToolDispatch(ToolCategory.StepEvidence)]
public class CompleteStepToolHandlers
{
    private static readonly FrozenSet<string> ValidKinds = FrozenSet.ToFrozenSet(
    [
        StepEvidenceKindConstants.Verification,
        StepEvidenceKindConstants.Diff,
        StepEvidenceKindConstants.Files,
        StepEvidenceKindConstants.Manual,
    ], StringComparer.OrdinalIgnoreCase);

    [McpTool(CompleteStepToolNameConstants.CompleteStep,
        "Record the evidence-backed completion of ONE step of an approved plan. Call it as you finish each step instead of silently moving on: it signs the step off with PROOF it is done — the verification you ran (command + result), the diff/files you changed, or a manual check. A completion with no evidence is REJECTED, so don't claim a step is done until you can show why. The host advances the task list for you when you sign off — it marks this step completed and moves the next to in_progress, so you don't need a separate TodoWrite to mark completions.",
        "todo")]
    public Task<ToolResult> CompleteStepAsync(
        [McpToolParameter("Which plan step this completes — its title or number, matching the task list")] string step,
        [McpToolParameter("What is now true or changed as a result of finishing this step")] string result,
        [McpToolParameter("Proof the step is done. At least one item is required. Each item has: kind (verification|diff|files|manual) and summary, plus optional command (REQUIRED for verification) and paths (REQUIRED for diff/files)", Required = false)] List<StepEvidenceInput>? evidence = null,
        [McpToolParameter("Optional caveats, follow-ups, or anything deferred", Required = false)] string? notes = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(step))
        {
            var diag = BuildEmptyStepDiagnostic();
            return Task.FromResult(ToolResultBuilder.Error()
                .WithText(diag.FormattedMessage).WithDiagnostic(diag).Build());
        }

        if (string.IsNullOrWhiteSpace(result))
        {
            var diag = BuildEmptyResultDiagnostic();
            return Task.FromResult(ToolResultBuilder.Error()
                .WithText(diag.FormattedMessage).WithDiagnostic(diag).Build());
        }

        if (evidence is null || evidence.Count == 0)
        {
            var diag = BuildNoEvidenceDiagnostic();
            return Task.FromResult(ToolResultBuilder.Error()
                .WithText(diag.FormattedMessage).WithDiagnostic(diag).Build());
        }

        var kinds = new List<string>(evidence.Count);
        for (var i = 0; i < evidence.Count; i++)
        {
            var e = evidence[i];

            if (!ValidKinds.Contains(e.Kind))
            {
                var diag = BuildInvalidKindDiagnostic(i + 1, e.Kind);
                return Task.FromResult(ToolResultBuilder.Error()
                    .WithText(diag.FormattedMessage).WithDiagnostic(diag).Build());
            }

            if (string.IsNullOrWhiteSpace(e.Summary))
            {
                var diag = BuildEmptySummaryDiagnostic(i + 1);
                return Task.FromResult(ToolResultBuilder.Error()
                    .WithText(diag.FormattedMessage).WithDiagnostic(diag).Build());
            }

            if (e.Kind.Equals(StepEvidenceKindConstants.Verification, StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(e.Command))
            {
                var diag = BuildMissingVerificationCommandDiagnostic(i + 1);
                return Task.FromResult(ToolResultBuilder.Error()
                    .WithText(diag.FormattedMessage).WithDiagnostic(diag).Build());
            }

            if ((e.Kind.Equals(StepEvidenceKindConstants.Diff, StringComparison.OrdinalIgnoreCase)
                 || e.Kind.Equals(StepEvidenceKindConstants.Files, StringComparison.OrdinalIgnoreCase))
                && (e.Paths is null || e.Paths.Count == 0))
            {
                var diag = BuildMissingPathsDiagnostic(i + 1, e.Kind);
                return Task.FromResult(ToolResultBuilder.Error()
                    .WithText(diag.FormattedMessage).WithDiagnostic(diag).Build());
            }

            kinds.Add(e.Kind);
        }

        var response = new StringBuilder();
        response.Append($"Step \"{step}\" signed off with {evidence.Count} evidence item(s) [{string.Join(", ", kinds)}].");

        if (!string.IsNullOrWhiteSpace(notes))
        {
            response.Append($" Notes: {notes}");
        }

        response.Append(" The host advanced the task list; continue with the next step.");

        return Task.FromResult(ToolResultBuilder.Success().WithText(response.ToString()).Build());
    }

    internal static ToolDiagnostic BuildEmptyStepDiagnostic() =>
        ToolDiagnostic.Create(
            reason: "参数验证失败",
            formattedMessage: "step is required — name the plan step you are completing",
            details: [new DiagnosticDetail("field", "step")]);

    internal static ToolDiagnostic BuildEmptyResultDiagnostic() =>
        ToolDiagnostic.Create(
            reason: "参数验证失败",
            formattedMessage: "result is required — state what is now true after finishing this step",
            details: [new DiagnosticDetail("field", "result")]);

    internal static ToolDiagnostic BuildNoEvidenceDiagnostic() =>
        ToolDiagnostic.Create(
            reason: "证据缺失",
            formattedMessage: "At least one evidence item is required — don't mark a step complete without showing why it's done (run a check, cite the diff, or confirm manually)",
            suggestions: ["运行验证命令", "引用 diff", "手动确认"]);

    internal static ToolDiagnostic BuildInvalidKindDiagnostic(int index, string kind) =>
        ToolDiagnostic.Create(
            reason: "参数验证失败",
            formattedMessage: $"evidence {index}: invalid kind '{kind}' (want verification|diff|files|manual)",
            details: [new DiagnosticDetail("evidence_index", index.ToString()), new DiagnosticDetail("kind", kind)],
            suggestions: ["使用 verification、diff、files 或 manual 之一"]);

    internal static ToolDiagnostic BuildEmptySummaryDiagnostic(int index) =>
        ToolDiagnostic.Create(
            reason: "参数验证失败",
            formattedMessage: $"evidence {index}: summary is required — the evidence is the summary, not just its kind",
            details: [new DiagnosticDetail("evidence_index", index.ToString())]);

    internal static ToolDiagnostic BuildMissingVerificationCommandDiagnostic(int index) =>
        ToolDiagnostic.Create(
            reason: "参数验证失败",
            formattedMessage: $"evidence {index}: verification command is required for verification evidence — cite the command you ran, or use kind \"manual\"",
            details: [new DiagnosticDetail("evidence_index", index.ToString())],
            suggestions: ["提供验证命令", "使用 kind \"manual\" 代替"]);

    internal static ToolDiagnostic BuildMissingPathsDiagnostic(int index, string kind) =>
        ToolDiagnostic.Create(
            reason: "参数验证失败",
            formattedMessage: $"evidence {index}: {kind} evidence requires paths — cite the files you changed or touched",
            details: [new DiagnosticDetail("evidence_index", index.ToString()), new DiagnosticDetail("kind", kind)],
            suggestions: ["提供变更文件的路径列表"]);
}
