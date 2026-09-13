namespace Core.Agents.Doctor;

/// <summary>
/// 源码补丁应用器 — 安全地应用源码修改并支持回滚
/// </summary>
public sealed class SourceCodePatcher
{
    private readonly IFileSystem _fs;

    /// <summary>
    /// 构造源码补丁应用器
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    public SourceCodePatcher(IFileSystem fs)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
    }

    /// <summary>
    /// 应用源码补丁 — 校验原内容匹配后写入新内容，支持安全回滚
    /// </summary>
    /// <param name="filePath">目标源码文件路径</param>
    /// <param name="originalContent">预期原始内容（用于安全校验，null 跳过校验）</param>
    /// <param name="patchedContent">补丁后的新内容</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>补丁应用结果</returns>
    public async Task<SourceCodePatchResult> ApplyPatchAsync(
        string filePath,
        string originalContent,
        string patchedContent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            if (!_fs.FileExists(filePath))
            {
                DoctorDiag.WriteError($"[Doctor] 源码文件不存在: {filePath}");
                return new SourceCodePatchResult
                {
                    Success = false,
                    FilePath = filePath,
                    Description = $"文件不存在: {filePath}",
                    Duration = sw.Elapsed
                };
            }

            var currentContent = await _fs.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);

            if (originalContent is not null && currentContent != originalContent)
            {
                DoctorDiag.WriteError($"[Doctor] 文件内容已变更，无法安全应用补丁: {filePath}");
                return new SourceCodePatchResult
                {
                    Success = false,
                    FilePath = filePath,
                    Description = "文件内容已变更，无法安全应用补丁",
                    Duration = sw.Elapsed
                };
            }

            await _fs.WriteAllTextAsync(filePath, patchedContent, cancellationToken).ConfigureAwait(false);

            sw.Stop();
            DoctorDiag.Write($"[Doctor] 源码补丁已应用: {filePath}");

            return new SourceCodePatchResult
            {
                Success = true,
                FilePath = filePath,
                Description = $"已修改 {filePath}",
                OriginalContent = currentContent,
                PatchedContent = patchedContent,
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            DoctorDiag.WriteError($"[Doctor] 应用源码补丁失败: {filePath}: {ex.Message}");
            return new SourceCodePatchResult
            {
                Success = false,
                FilePath = filePath,
                Description = $"应用补丁异常: {ex.Message}",
                Duration = sw.Elapsed
            };
        }
    }

    /// <summary>
    /// 回滚源码补丁 — 将文件内容恢复为原始内容
    /// </summary>
    /// <param name="filePath">目标源码文件路径</param>
    /// <param name="originalContent">要恢复的原始内容</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>回滚结果</returns>
    public async Task<SourceCodePatchResult> RollbackAsync(
        string filePath,
        string originalContent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await _fs.WriteAllTextAsync(filePath, originalContent, cancellationToken).ConfigureAwait(false);

            sw.Stop();
            DoctorDiag.Write($"[Doctor] 源码补丁已回滚: {filePath}");

            return new SourceCodePatchResult
            {
                Success = true,
                FilePath = filePath,
                Description = $"已回滚 {filePath}",
                OriginalContent = originalContent,
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            DoctorDiag.WriteError($"[Doctor] 回滚源码补丁失败: {filePath}: {ex.Message}");
            return new SourceCodePatchResult
            {
                Success = false,
                FilePath = filePath,
                Description = $"回滚异常: {ex.Message}",
                Duration = sw.Elapsed
            };
        }
    }
}

/// <summary>
/// 源码补丁应用结果
/// </summary>
public sealed record SourceCodePatchResult
{
    /// <summary>是否操作成功</summary>
    public required bool Success { get; init; }

    /// <summary>目标文件路径</summary>
    public required string FilePath { get; init; }

    /// <summary>结果描述</summary>
    public string? Description { get; init; }

    /// <summary>原始内容</summary>
    public string? OriginalContent { get; init; }

    /// <summary>补丁后内容</summary>
    public string? PatchedContent { get; init; }

    /// <summary>操作耗时</summary>
    public TimeSpan Duration { get; init; }
}
