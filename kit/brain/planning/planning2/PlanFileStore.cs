namespace Core.Planning;

/// <summary>
/// 计划文件存储 — 管理计划文件的持久化、清理、格式化
/// 从 PlanModeManager 提取,降低大类复杂度
/// </summary>
internal sealed class PlanFileStore
{
    private readonly IFileSystem _fs;
    private readonly IClockService _clock;
    private readonly ILogger? _logger;

    /// <summary>初始化 <see cref="PlanFileStore"/> 实例</summary>
    public PlanFileStore(IFileSystem fs, IClockService clock, ILogger? logger = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger;
    }

    // ── 静态工具方法 ──

    /// <summary>获取 Plan 文件路径 — 路径格式: ~/.jcc/plans/{slug}.md</summary>
    public static string GetPlanFilePath(string slug)
        => Path.Combine(PlanSlugGenerator.GetPlansDirectory(), $"{slug}.md");

    /// <summary>跨进程持久化文件路径</summary>
    public static string GetActivePlanStateFilePath()
        => Path.Combine(PlanSlugGenerator.GetPlansDirectory(), ".active_plan_state.json");

    /// <summary>获取已删除文件的归档路径</summary>
    public static string GetDeletedPath(string filePath, DateTime timestamp)
    {
        var dir = Path.GetDirectoryName(filePath) ?? ".";
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var ext = Path.GetExtension(filePath);
        var ts = timestamp.ToString("yyyyMMddHHmmss");
        return Path.Combine(dir, ".x", $"{fileName}{ext}.{ts}.del");
    }

    /// <summary>将 Plan 格式化为 Markdown</summary>
    public static string FormatPlanAsMarkdown(PlanState plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Plan: {plan.Description ?? "Untitled"}");
        sb.AppendLine();
        sb.AppendLine($"- **Plan ID**: {plan.PlanId}");
        sb.AppendLine($"- **Status**: {plan.Status}");
        sb.AppendLine($"- **Created**: {plan.CreatedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"- **Updated**: {plan.LastUpdatedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"- **Progress**: {plan.CompletedStepsCount}/{plan.TotalSteps} ({plan.GetProgressPercentage():F1}%)");
        sb.AppendLine();

        if (plan.Steps.Count > 0)
        {
            sb.AppendLine("## Steps");
            sb.AppendLine();
            foreach (var step in plan.Steps)
            {
                var statusIcon = step.Status switch
                {
                    PlanStepStatus.Pending => "[ ]",
                    PlanStepStatus.Approved => "[~]",
                    PlanStepStatus.Rejected => "[x]",
                    PlanStepStatus.Executing => "[>]",
                    PlanStepStatus.Completed => "[✓]",
                    PlanStepStatus.Failed => "[✗]",
                    PlanStepStatus.Skipped => "[-]",
                    _ => "[?]"
                };

                var toolInfo = !string.IsNullOrEmpty(step.ToolName) ? $" (`{step.ToolName}`)" : "";
                sb.AppendLine($"- {statusIcon} {step.Description}{toolInfo}");

                if (!string.IsNullOrEmpty(step.ExecutionResult))
                {
                    sb.AppendLine($"  - Result: {step.ExecutionResult}");
                }
                if (!string.IsNullOrEmpty(step.RejectionReason))
                {
                    sb.AppendLine($"  - Reason: {step.RejectionReason}");
                }
            }
        }

        return sb.ToString();
    }

    // ── 实例方法 ──

    /// <summary>清理旧计划文件（返回清理数量）</summary>
    public int CleanupOldFiles(int maxAgeDays = 30)
    {
        var plansDir = PlanSlugGenerator.GetPlansDirectory();
        if (!_fs.DirectoryExists(plansDir))
        {
            return 0;
        }

        var cutoff = _clock.GetUtcNow().AddDays(-maxAgeDays);
        var cleanedCount = 0;

        try
        {
            foreach (var filePath in _fs.EnumerateFiles(plansDir, "*.md", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var lastWriteTime = _fs.GetLastWriteTimeUtc(filePath);
                    if (lastWriteTime < cutoff)
                    {
                        var deletedPath = GetDeletedPath(filePath, _clock.GetUtcNow());
                        var deletedDir = Path.GetDirectoryName(deletedPath)!;
                        if (!_fs.DirectoryExists(deletedDir))
                        {
                            _fs.CreateDirectory(deletedDir);
                        }
                        _fs.MoveFile(filePath, deletedPath);
                        cleanedCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning("计划文件清理失败: {Error}", ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("计划目录遍历失败: {Error}", ex.Message);
        }

        return cleanedCount;
    }

    /// <summary>清除活跃 plan 状态文件</summary>
    public void ClearActivePlanStateFile()
    {
        var filePath = GetActivePlanStateFilePath();
        if (_fs.FileExists(filePath))
        {
            try
            {
                _fs.DeleteFile(filePath);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("清除活跃 plan 状态文件失败: {Error}", ex.Message);
            }
        }
    }

    /// <summary>保存活跃 plan 状态到文件</summary>
    public async Task SaveActivePlanStateAsync(string planId, string? sessionSlug, PlanState plan, CancellationToken cancellationToken)
    {
        var filePath = GetActivePlanStateFilePath();
        var state = new PersistablePlanState
        {
            CurrentPlanId = planId,
            CurrentSessionSlug = sessionSlug,
            Plan = plan
        };

        try
        {
            var dir = Path.GetDirectoryName(filePath)!;
            if (!_fs.DirectoryExists(dir))
                _fs.CreateDirectory(dir);
            var json = RelaxedJsonSerializer.Serialize(state, PlanJsonContext.Default);
            await _fs.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("保存活跃 plan 状态文件失败: {Error}", ex.Message);
        }
    }

    /// <summary>从文件加载活跃 plan 状态（未找到或失败返回 null）</summary>
    public async Task<PersistablePlanState?> LoadActivePlanStateAsync(CancellationToken cancellationToken)
    {
        var filePath = GetActivePlanStateFilePath();
        if (!_fs.FileExists(filePath)) return null;

        try
        {
            var json = await _fs.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            return RelaxedJsonSerializer.Deserialize(json, PlanJsonContext.Default.PersistablePlanState);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("加载活跃 plan 状态文件失败: {Error}", ex.Message);
            return null;
        }
    }
}
