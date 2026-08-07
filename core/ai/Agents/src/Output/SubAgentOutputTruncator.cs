namespace Core.Agents;

/// <summary>
/// 子智能体输出截断器 — L3 落盘指针兜底
/// <para>当子智能体输出超过主智能体剩余预算时，完整落盘到 .xxx/subagent/，只返回指针+概要（不半截）。</para>
/// <para>对齐 openCode Truncate.output，但去掉半截预览，只给指针，要看就 read 全文。</para>
/// </summary>
[Register]
public sealed partial class SubAgentOutputTruncator : ServiceEntity
{
    private const int CharsPerToken = 4;
    private const string ArchiveSubdir = ".xxx";
    private const string ArchiveLeaf = "subagent";

    [Inject] private readonly ILogger<SubAgentOutputTruncator> _logger;
    private readonly IFileSystem _fs;
    private readonly string _archiveDir;

    public SubAgentOutputTruncator(IFileSystem fs, ILogger<SubAgentOutputTruncator> logger, string? archiveDir = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _logger = logger;
        _archiveDir = archiveDir ?? Path.Combine(_fs.GetCurrentDirectory(), ArchiveSubdir, ArchiveLeaf);
    }

    /// <summary>
    /// 截断子智能体输出。若在预算内原样返回；超预算则落盘并返回指针。
    /// </summary>
    /// <param name="agentId">子智能体标识</param>
    /// <param name="output">子智能体完整输出文本</param>
    /// <param name="remainingTokenBudget">主智能体剩余 token 预算</param>
    /// <param name="summary">一句话概要（可选），来自 L0 包装</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task<SubAgentOutputTruncationResult> TruncateAsync(
        string agentId,
        string output,
        int remainingTokenBudget,
        string? summary = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(output))
            return new SubAgentOutputTruncationResult(output, null, false);

        var outputTokens = EstimateTokens(output);
        if (outputTokens <= remainingTokenBudget)
            return new SubAgentOutputTruncationResult(output, null, false);

        var archivedPath = await ArchiveAsync(agentId, output, cancellationToken).ConfigureAwait(false);
        var lineCount = output.Count(c => c == '\n') + 1;
        var pointer = BuildPointer(agentId, lineCount, outputTokens, summary, archivedPath);
        return new SubAgentOutputTruncationResult(pointer, archivedPath, true);
    }

    /// <summary>
    /// 估算 token 数 — 对齐项目 CharsPerToken=4
    /// </summary>
    public static int EstimateTokens(string text) => text.Length / CharsPerToken;

    private static string BuildPointer(string agentId, int lineCount, int tokenCount, string? summary, string path)
    {
        var summaryPart = string.IsNullOrWhiteSpace(summary) ? "" : $"，概要：{summary}";
        return $"[子智能体 {agentId} 报告 {lineCount}行/{tokenCount}token{summaryPart}。完整存档 {path}，read 查看]";
    }

    private async Task<string> ArchiveAsync(string agentId, string output, CancellationToken cancellationToken)
    {
        if (!_fs.DirectoryExists(_archiveDir))
            _fs.CreateDirectory(_archiveDir);

        var safeId = SanitizeAgentId(agentId);
        var fileName = $"{safeId}_{DateTime.UtcNow:yyyyMMddHHmmssfff}.md";
        var fullPath = Path.Combine(_archiveDir, fileName);
        await _fs.WriteAllTextAsync(fullPath, output, cancellationToken).ConfigureAwait(false);
        return fullPath;
    }

    private static string SanitizeAgentId(string agentId)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(agentId.Length);
        foreach (var c in agentId)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString();
    }
}
