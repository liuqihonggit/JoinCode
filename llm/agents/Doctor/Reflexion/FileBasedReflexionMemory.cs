namespace Core.Agents.Doctor;


/// <summary>
/// 基于文件的反思记忆 — 将修复经验序列化为 JSON 文件存储
/// 路径: .jcc/reflexion/{RuleId}/{timestamp}.json
/// </summary>
public sealed class FileBasedReflexionMemory : IReflexionMemory
{
    private readonly IFileSystem _fs;
    private readonly string _baseDir;

    /// <summary>
    /// 构造基于文件的反思记忆
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="baseDir">存储基目录（可选，默认使用 AppDataConstants.Paths.ReflexionDirectory）</param>
    public FileBasedReflexionMemory(IFileSystem fs, string? baseDir = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _baseDir = baseDir ?? AppDataConstants.Paths.ReflexionDirectory;
    }

    /// <summary>
    /// 存储修复经验 — 按 RuleId 分目录序列化为 JSON 文件
    /// </summary>
    /// <param name="patch">代码补丁</param>
    /// <param name="diagnostic">诊断报告</param>
    /// <param name="wasSuccessful">本次修复是否成功</param>
    /// <param name="ct">取消令牌</param>
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
        var json = RelaxedJsonSerializer.Serialize(entry, ReflexionEntryJsonContext.Default);

        await _fs.WriteAllTextAsync(filePath, json, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 检索与当前诊断相似的历史补丁 — 按 RuleId 匹配，优先返回最近成功的补丁
    /// </summary>
    /// <param name="diagnostic">当前诊断报告</param>
    /// <param name="maxResults">最大返回数量（默认 3）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>相似历史补丁列表</returns>
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

            var tasks = files.Select(async file =>
            {
                try
                {
                    var json = await _fs.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                    var entry = RelaxedJsonSerializer.Deserialize(json, ReflexionEntryJsonContext.Default.ReflexionEntry);
                    return entry is not null && entry.WasSuccessful ? entry.Patch : null;
                }
                catch (Exception ex)
                {
                    DoctorDiag.WriteError($"[Doctor] 读取反思记忆失败: {file}: {ex.Message}");
                    return null;
                }
            }).ToArray();

            var taskResults = await Task.WhenAll(tasks).ConfigureAwait(false);
            results.AddRange(taskResults.Where(r => r is not null).Cast<CodePatch>());
        }
        catch (Exception ex)
        {
            DoctorDiag.WriteError($"[Doctor] 检索反思记忆失败: {ex.Message}");
        }

        return results;
    }

    /// <summary>
    /// 获取所有规则的反思统计 — 汇总每个 RuleId 的尝试次数、成功/失败数、最后尝试时间
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>规则统计列表</returns>
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

                var fileTasks = files.Select(async file =>
                {
                    try
                    {
                        var json = await _fs.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                        return RelaxedJsonSerializer.Deserialize(json, ReflexionEntryJsonContext.Default.ReflexionEntry);
                    }
                    catch (Exception ex)
                    {
                        DoctorDiag.WriteError($"[Doctor] 读取反思统计失败: {file}: {ex.Message}");
                        return null;
                    }
                }).ToArray();

                var entries = await Task.WhenAll(fileTasks).ConfigureAwait(false);
                foreach (var entry in entries)
                {
                    if (entry is null) continue;

                    totalAttempts++;
                    if (entry.WasSuccessful) successfulPatches++;
                    else failedPatches++;

                    if (entry.StoredAt > lastAttemptAt)
                        lastAttemptAt = entry.StoredAt;
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

/// <summary>
/// 反思记忆条目 — 单次修复经验的序列化记录
/// </summary>
internal sealed record ReflexionEntry
{
    /// <summary>代码补丁</summary>
    public required CodePatch Patch { get; init; }

    /// <summary>诊断摘要</summary>
    public required ReflexionDiagnosticSummary Diagnostic { get; init; }

    /// <summary>本次修复是否成功</summary>
    public required bool WasSuccessful { get; init; }

    /// <summary>存储时间</summary>
    public required DateTimeOffset StoredAt { get; init; }
}

/// <summary>
/// 反思诊断摘要 — 诊断报告的精简序列化形式
/// </summary>
internal sealed record ReflexionDiagnosticSummary
{
    /// <summary>诊断规则 ID</summary>
    public required DiagnosticRuleId RuleId { get; init; }

    /// <summary>严重级别</summary>
    public required DiagnosticSeverity Severity { get; init; }

    /// <summary>诊断描述</summary>
    public required string Description { get; init; }
}

/// <summary>
/// ReflexionEntry 专用 JSON 序列化上下文 — AOT 源码生成
/// </summary>
[JsonSerializable(typeof(ReflexionEntry))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
internal sealed partial class ReflexionEntryJsonContext : JsonSerializerContext;
