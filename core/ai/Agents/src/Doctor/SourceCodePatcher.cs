namespace Core.Agents.Doctor;

public sealed class SourceCodePatcher
{
    private readonly IFileSystem _fs;

    public SourceCodePatcher(IFileSystem fs)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
    }

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

public sealed record SourceCodePatchResult
{
    public required bool Success { get; init; }
    public required string FilePath { get; init; }
    public string? Description { get; init; }
    public string? OriginalContent { get; init; }
    public string? PatchedContent { get; init; }
    public TimeSpan Duration { get; init; }
}
