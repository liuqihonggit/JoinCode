namespace Services.StepEvidence.ToolHandlers;

/// <summary>
/// 步骤完成证据驱动工具 — 对齐 Reasonix complete_step
/// 强制模型在标记步骤完成时必须提供证据，无证据的完成被拒绝。
/// 与 TodoWrite 互补：TodoWrite 管理任务列表状态，complete_step 是步骤的正式签收。
/// </summary>
[McpToolHandler(ToolCategory.StepEvidence)]
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
            return Task.FromResult(McpResultBuilder.Error()
                .WithText("step is required — name the plan step you are completing").Build());
        }

        if (string.IsNullOrWhiteSpace(result))
        {
            return Task.FromResult(McpResultBuilder.Error()
                .WithText("result is required — state what is now true after finishing this step").Build());
        }

        if (evidence is null || evidence.Count == 0)
        {
            return Task.FromResult(McpResultBuilder.Error()
                .WithText("At least one evidence item is required — don't mark a step complete without showing why it's done (run a check, cite the diff, or confirm manually)").Build());
        }

        var kinds = new List<string>(evidence.Count);
        for (var i = 0; i < evidence.Count; i++)
        {
            var e = evidence[i];

            if (!ValidKinds.Contains(e.Kind))
            {
                return Task.FromResult(McpResultBuilder.Error()
                    .WithText($"evidence {i + 1}: invalid kind '{e.Kind}' (want verification|diff|files|manual)").Build());
            }

            if (string.IsNullOrWhiteSpace(e.Summary))
            {
                return Task.FromResult(McpResultBuilder.Error()
                    .WithText($"evidence {i + 1}: summary is required — the evidence is the summary, not just its kind").Build());
            }

            if (e.Kind.Equals(StepEvidenceKindConstants.Verification, StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(e.Command))
            {
                return Task.FromResult(McpResultBuilder.Error()
                    .WithText($"evidence {i + 1}: verification command is required for verification evidence — cite the command you ran, or use kind \"manual\"").Build());
            }

            if ((e.Kind.Equals(StepEvidenceKindConstants.Diff, StringComparison.OrdinalIgnoreCase)
                 || e.Kind.Equals(StepEvidenceKindConstants.Files, StringComparison.OrdinalIgnoreCase))
                && (e.Paths is null || e.Paths.Count == 0))
            {
                return Task.FromResult(McpResultBuilder.Error()
                    .WithText($"evidence {i + 1}: {e.Kind} evidence requires paths — cite the files you changed or touched").Build());
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

        return Task.FromResult(McpResultBuilder.Success().WithText(response.ToString()).Build());
    }
}
