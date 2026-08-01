namespace Core.Agents.Doctor;

using JoinCode.Abstractions.Interfaces.Doctor;

/// <summary>
/// 自举闭环 — 源码工程驱动的自我修复
/// 核心链路: 诊断 → 定位源码 → worktree 隔离 → LLM 生成 patch → Guard 审核 → 编译 → 替换 exe
/// </summary>
public sealed class BootstrapLoop
{
    private readonly ISourceCodeEngine _sourceEngine;
    private readonly IBootstrapWorktreeManager _worktreeMgr;
    private readonly ICodePatchGenerator _patchGenerator;
    private readonly IBootstrapGuard _guard;
    private readonly IReflexionMemory? _memory;
    private readonly IFileSystem _fs;

    public BootstrapLoop(
        ISourceCodeEngine sourceEngine,
        IBootstrapWorktreeManager worktreeMgr,
        ICodePatchGenerator patchGenerator,
        IBootstrapGuard guard,
        IFileSystem fs,
        IReflexionMemory? memory = null)
    {
        _sourceEngine = sourceEngine ?? throw new ArgumentNullException(nameof(sourceEngine));
        _worktreeMgr = worktreeMgr ?? throw new ArgumentNullException(nameof(worktreeMgr));
        _patchGenerator = patchGenerator ?? throw new ArgumentNullException(nameof(patchGenerator));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _memory = memory;
    }

    public async Task<BootstrapResult> ExecuteAsync(
        DiagnosticReport diagnostic,
        string? workingDirectory = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        var startedAt = DateTimeOffset.UtcNow;

        DoctorDiag.Write($"[Doctor] 自举闭环启动: {diagnostic.RuleId} - {diagnostic.Description}");

        try
        {
            var sourceLocation = await _sourceEngine.LocateSourceRepositoryAsync(workingDirectory, ct).ConfigureAwait(false);
            if (!sourceLocation.IsAvailable)
                return Fail(startedAt, $"无法定位源码仓库: {sourceLocation.FailureReason}");

            var worktree = await _worktreeMgr.CreateAsync(sourceLocation.GitRoot, ct: ct).ConfigureAwait(false);

            var targetFile = LocateTargetFile(diagnostic, worktree.WorktreePath);
            if (targetFile is null)
                return Fail(startedAt, "无法定位目标源码文件");

            var currentContent = await ReadFileContentAsync(targetFile, ct).ConfigureAwait(false);
            if (currentContent is null)
                return Fail(startedAt, $"无法读取目标文件: {targetFile}");

            var sourceContext = new SourceCodeContext
            {
                FilePath = targetFile,
                CurrentContent = currentContent
            };

            var historicalPatches = _memory is not null
                ? await _memory.RetrieveSimilarPatchesAsync(diagnostic, ct: ct).ConfigureAwait(false)
                : null;

            var patch = await _patchGenerator.GeneratePatchAsync(diagnostic, sourceContext, historicalPatches, ct).ConfigureAwait(false);

            if (patch.Confidence < 0.3)
                return Fail(startedAt, $"LLM 生成的 patch 置信度过低: {patch.Confidence:F2}");

            var guardDecision = await _guard.ReviewAsync(new BootstrapModificationRequest
            {
                ModificationType = BootstrapFixType.SourceCodePatch,
                TargetPath = targetFile,
                OriginalContent = currentContent,
                ProposedContent = patch.PatchedContent,
                Justification = patch.Reasoning ?? patch.Description
            }).ConfigureAwait(false);

            if (!guardDecision.Approved)
                return Fail(startedAt, $"安全审核未通过: {guardDecision.Reason}");

            await WriteFileContentAsync(targetFile, patch.PatchedContent, ct).ConfigureAwait(false);

            var buildResult = await _sourceEngine.BuildFullProjectAsync(worktree.WorktreePath, "Debug", ct).ConfigureAwait(false);
            if (!buildResult.Success)
            {
                DoctorDiag.WriteError($"[Doctor] 编译失败，回滚修改: 第 {buildResult.FirstFailedLayer} 层");
                await WriteFileContentAsync(targetFile, currentContent, ct).ConfigureAwait(false);
                return Fail(startedAt, $"编译失败: 第 {buildResult.FirstFailedLayer} 层");
            }

            if (_memory is not null)
            {
                await _memory.StoreAsync(patch, diagnostic, wasSuccessful: true, ct).ConfigureAwait(false);
            }

            DoctorDiag.Write($"[Doctor] 自举闭环成功: {diagnostic.RuleId}");

            return new BootstrapResult
            {
                Success = true,
                Diagnostic = diagnostic,
                Patch = patch,
                BuildResult = buildResult,
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow
            };
        }
        catch (OperationCanceledException)
        {
            return Fail(startedAt, "自举闭环被取消");
        }
        catch (Exception ex)
        {
            DoctorDiag.WriteError($"[Doctor] 自举闭环异常: {ex.Message}");
            return Fail(startedAt, $"自举闭环异常: {ex.Message}");
        }
        finally
        {
            await _worktreeMgr.CleanupAsync(ct).ConfigureAwait(false);
        }
    }

    private static BootstrapResult Fail(DateTimeOffset startedAt, string reason)
    {
        DoctorDiag.WriteError($"[Doctor] 自举闭环失败: {reason}");
        return new BootstrapResult
        {
            Success = false,
            FailureReason = reason,
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow
        };
    }

    private static string? LocateTargetFile(DiagnosticReport diagnostic, string worktreePath)
    {
        if (diagnostic.TriggeringEvents.Count > 0)
        {
            foreach (var evt in diagnostic.TriggeringEvents)
            {
                if (evt.Properties.TryGetValue("source_file", out var file) && !string.IsNullOrWhiteSpace(file))
                    return file;
                if (evt.Properties.TryGetValue("file_path", out var path) && !string.IsNullOrWhiteSpace(path))
                    return path;
            }
        }

        return null;
    }

    private async Task<string?> ReadFileContentAsync(string filePath, CancellationToken ct)
    {
        try
        {
            return await _fs.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            DoctorDiag.WriteError($"[Doctor] 读取文件失败: {filePath}: {ex.Message}");
            return null;
        }
    }

    private async Task WriteFileContentAsync(string filePath, string content, CancellationToken ct)
    {
        await _fs.WriteAllTextAsync(filePath, content, ct).ConfigureAwait(false);
    }
}

/// <summary>
/// 自举结果
/// </summary>
public sealed record BootstrapResult
{
    /// <summary>是否成功</summary>
    public required bool Success { get; init; }

    /// <summary>诊断报告</summary>
    public DiagnosticReport? Diagnostic { get; init; }

    /// <summary>生成的 patch</summary>
    public CodePatch? Patch { get; init; }

    /// <summary>编译结果</summary>
    public FullBuildResult? BuildResult { get; init; }

    /// <summary>失败原因</summary>
    public string? FailureReason { get; init; }

    /// <summary>开始时间</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>完成时间</summary>
    public DateTimeOffset CompletedAt { get; init; }
}
