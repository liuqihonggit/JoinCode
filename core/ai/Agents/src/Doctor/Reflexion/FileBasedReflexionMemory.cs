namespace Core.Agents.Doctor;


/// <summary>
/// 基于文件的反思记忆 — 将修复经验序列化为 JSON 文件存储
/// 路径: .jcc/reflexion/{RuleId}/{timestamp}.json
/// </summary>
public sealed class FileBasedReflexionMemory : IReflexionMemory
{
    private readonly IFileSystem _fs;
    private readonly string _baseDir;

    public FileBasedReflexionMemory(IFileSystem fs, string? baseDir = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _baseDir = baseDir ?? Path.Combine(_fs.GetCurrentDirectory(), ".jcc", "reflexion");
    }

    public async Task StoreAsync(
        CodePatch patch,
        DiagnosticReport diagnostic,
        bool wasSuccessful,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(patch);
        ArgumentNullException.ThrowIfNull(diagnostic);

        var ruleDir = Path.Combine(_baseDir, diagnostic.RuleId.ToString());
        _fs.CreateDirectory(ruleDir);

        var entry = new ReflexionEntry
        {
            Patch = patch,
            Diagnostic = new ReflexionDiagnosticSummary
            {
                RuleId = diagnostic.RuleId,
                Severity = diagnostic.Severity,
                Description = diagnostic.Description
            },
            WasSuccessful = wasSuccessful,
            StoredAt = DateTimeOffset.UtcNow
        };

        var fileName = $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}.json";
        var filePath = Path.Combine(ruleDir, fileName);
        var json = JsonSerializer.Serialize(entry, ReflexionEntryJsonContext.Default.ReflexionEntry);

        await _fs.WriteAllTextAsync(filePath, json, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CodePatch>> RetrieveSimilarPatchesAsync(
        DiagnosticReport diagnostic,
        int maxResults = 3,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        var ruleDir = Path.Combine(_baseDir, diagnostic.RuleId.ToString());
        if (!_fs.DirectoryExists(ruleDir))
            return [];

        var results = new List<CodePatch>();

        try
        {
            var files = _fs.EnumerateFiles(ruleDir, "*.json", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => f)
                .Take(maxResults);

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var json = await _fs.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                    var entry = JsonSerializer.Deserialize(json, ReflexionEntryJsonContext.Default.ReflexionEntry);
                    if (entry is not null && entry.WasSuccessful)
                        results.Add(entry.Patch);
                }
                catch (Exception ex)
                {
                    DoctorDiag.WriteError($"[Doctor] 读取反思记忆失败: {file}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            DoctorDiag.WriteError($"[Doctor] 检索反思记忆失败: {ex.Message}");
        }

        return results;
    }

    public async Task<IReadOnlyList<ReflexionRuleStats>> GetStatisticsAsync(CancellationToken ct = default)
    {
        if (!_fs.DirectoryExists(_baseDir))
            return [];

        var stats = new List<ReflexionRuleStats>();

        try
        {
            foreach (var ruleDir in _fs.EnumerateDirectories(_baseDir, "*", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();

                var ruleId = Path.GetFileName(ruleDir);
                if (string.IsNullOrEmpty(ruleId)) continue;

                var files = _fs.EnumerateFiles(ruleDir, "*.json", SearchOption.TopDirectoryOnly).ToList();
                if (files.Count == 0) continue;

                var totalAttempts = 0;
                var successfulPatches = 0;
                var failedPatches = 0;
                var lastAttemptAt = DateTimeOffset.MinValue;

                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        var json = await _fs.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                        var entry = JsonSerializer.Deserialize(json, ReflexionEntryJsonContext.Default.ReflexionEntry);
                        if (entry is null) continue;

                        totalAttempts++;
                        if (entry.WasSuccessful) successfulPatches++;
                        else failedPatches++;

                        if (entry.StoredAt > lastAttemptAt)
                            lastAttemptAt = entry.StoredAt;
                    }
                    catch (Exception ex)
                    {
                        DoctorDiag.WriteError($"[Doctor] 读取反思统计失败: {file}: {ex.Message}");
                    }
                }

                if (totalAttempts > 0)
                {
                    stats.Add(new ReflexionRuleStats
                    {
                        RuleId = ruleId,
                        TotalAttempts = totalAttempts,
                        SuccessfulPatches = successfulPatches,
                        FailedPatches = failedPatches,
                        LastAttemptAt = lastAttemptAt
                    });
                }
            }
        }
        catch (Exception ex)
        {
            DoctorDiag.WriteError($"[Doctor] 统计反思记忆失败: {ex.Message}");
        }

        return stats;
    }
}

internal sealed record ReflexionEntry
{
    public required CodePatch Patch { get; init; }
    public required ReflexionDiagnosticSummary Diagnostic { get; init; }
    public required bool WasSuccessful { get; init; }
    public required DateTimeOffset StoredAt { get; init; }
}

internal sealed record ReflexionDiagnosticSummary
{
    public required DiagnosticRuleId RuleId { get; init; }
    public required DiagnosticSeverity Severity { get; init; }
    public required string Description { get; init; }
}

[JsonSerializable(typeof(ReflexionEntry))]
[JsonSourceGenerationOptions(AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
internal sealed partial class ReflexionEntryJsonContext : JsonSerializerContext;
