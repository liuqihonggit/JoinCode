namespace Memdir.Services;

/// <summary>
/// 会话扫描器 — 扫描 ~/.jcc/sessions/ 下所有 .json 会话文件，提取洞察元数据
/// 对齐 TS insights.ts scanAllSessions + logToSessionMeta + extractToolStats
/// </summary>
[Register(typeof(IInsightSessionScanner), ServiceLifetime.Singleton)]
public sealed partial class SessionScanner : ServiceEntity, IInsightSessionScanner {
    private readonly string _sessionsDirectory;
    private readonly ILogger<SessionScanner>? _logger;
    private readonly IFileSystem _fs;

    /// <summary>文件扩展名到语言名的映射 — 委托 LanguageMapCatalog 单一数据源</summary>
    private static readonly IReadOnlyDictionary<string, string> ExtensionToLanguage = LanguageMapCatalog.ExtensionToLanguage;

    /// <summary>
    /// 创建会话扫描器实例
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="sessionsDirectory">会话文件目录路径,默认为 ~/.jcc/sessions/</param>
    /// <param name="logger">可选的日志记录器</param>
    public SessionScanner(IFileSystem fs, string? sessionsDirectory = null, ILogger<SessionScanner>? logger = null) {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _sessionsDirectory = sessionsDirectory
            ?? Path.Combine(
                AppDataConstants.Paths.JccDirectory,
                AppDataConstants.SessionsFolderName);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InsightSessionMeta>> ScanAllSessionsAsync(CancellationToken cancellationToken = default) {
        if (!_fs.DirectoryExists(_sessionsDirectory)) {
            return Array.Empty<InsightSessionMeta>();
        }

        var files = _fs.EnumerateFiles(_sessionsDirectory, "*.json", SearchOption.TopDirectoryOnly);
        var tasks = files.Select(async file => {
            try {
                return await ExtractSessionMetaAsync(file, cancellationToken).ConfigureAwait(false);
            } catch (OperationCanceledException) { throw; } catch (Exception ex) {
                _logger?.LogWarning(ex, "跳过无法读取的会话文件: {File}", file);
                return null;
            }
        }).ToArray();

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.Where(r => r is not null).Cast<InsightSessionMeta>().ToList();
    }

    /// <summary>
    /// 从单个 JSONL 文件提取 InsightSessionMeta — 对齐 TS logToSessionMeta + extractToolStats
    /// </summary>
    private async Task<InsightSessionMeta?> ExtractSessionMetaAsync(string filePath, CancellationToken cancellationToken) {
        var sessionId = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrEmpty(sessionId)) return null;

        var entries = await ReadEntriesAsync(_fs, filePath, cancellationToken, _logger).ConfigureAwait(false);
        if (entries.Count == 0) return null;

        var lastWriteTimeUtc = _fs.GetLastWriteTimeUtc(filePath);
        var creationTimeUtc = _fs.GetCreationTimeUtc(filePath);
        var durationMinutes = (lastWriteTimeUtc - creationTimeUtc).TotalMinutes;
        if (durationMinutes < 0) durationMinutes = 0;

        // 统计 — 对齐 TS extractToolStats
        var toolCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var languages = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var toolErrorCategories = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var modifiedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var userMessageTimestamps = new List<DateTime>();
        DateTime? lastAssistantTime = null;

        var userMessageCount = 0;
        var assistantMessageCount = 0;
        long inputTokens = 0;
        long outputTokens = 0;
        var gitCommits = 0;
        var gitPushes = 0;
        var linesAdded = 0;
        var linesRemoved = 0;
        var userInterruptions = 0;
        var toolErrors = 0;
        var usesTaskAgent = false;
        var usesMcp = false;
        var usesWebSearch = false;
        var usesWebFetch = false;
        string? firstPrompt = null;
        decimal estimatedCost = 0;

        foreach (var entry in entries) {
            var role = entry.Role;

            // 助手消息统计
            if (string.Equals(role, MessageRoleEnumConstants.Assistant, StringComparison.OrdinalIgnoreCase)) {
                ProcessAssistantEntry(entry, toolCounts,
                    ref assistantMessageCount, ref inputTokens, ref outputTokens, ref lastAssistantTime,
                    ref usesMcp, ref usesWebSearch, ref usesWebFetch, ref usesTaskAgent);
            }

            // 用户消息统计
            if (string.Equals(role, MessageRoleEnumConstants.User, StringComparison.OrdinalIgnoreCase)) {
                ProcessUserEntry(entry, ref userMessageCount, ref firstPrompt, userMessageTimestamps, ref userInterruptions);
            }

            // 工具结果中的错误统计
            if (string.Equals(role, MessageRoleEnumConstants.Tool, StringComparison.OrdinalIgnoreCase) ||
                entry.Type == "tool_result") {
                ProcessToolResultEntry(entry, toolErrorCategories, languages, modifiedFiles,
                    ref toolErrors, ref gitCommits, ref gitPushes, ref linesAdded, ref linesRemoved);
            }
        }

        return BuildSessionMeta(sessionId, creationTimeUtc, durationMinutes,
            userMessageCount, assistantMessageCount, inputTokens, outputTokens,
            toolCounts, languages, gitCommits, gitPushes, linesAdded, linesRemoved,
            modifiedFiles, userInterruptions, toolErrors, toolErrorCategories,
            usesTaskAgent, usesMcp, usesWebSearch, usesWebFetch, firstPrompt, estimatedCost, userMessageTimestamps);
    }

    /// <summary>
    /// 安全累加 Token 数 — 溢出时钳制到 long.MaxValue,避免回绕为负数
    /// </summary>
    /// <param name="current">当前累计值</param>
    /// <param name="addition">本次增量(int 范围,单次不会溢出 long)</param>
    /// <returns>累加结果,溢出时返回 long.MaxValue</returns>
    internal static long SafeAddTokens(long current, int addition) {
        if (addition <= 0) return current;
        if (current > long.MaxValue - addition) return long.MaxValue;
        return current + addition;
    }

    /// <summary>
    /// 安全舍入时长 — NaN/Infinity 返回 0,否则四舍五入到1位小数
    /// </summary>
    /// <param name="durationMinutes">时长(分钟)</param>
    /// <returns>舍入后的时长,异常值返回 0</returns>
    internal static double SafeRoundDuration(double durationMinutes) {
        if (double.IsNaN(durationMinutes) || double.IsInfinity(durationMinutes)) return 0;
        return Math.Round(durationMinutes, 1);
    }

    /// <summary>
    /// 处理助手消息条目 — 统计消息数/Token/工具使用/特殊工具检测 — 纯计算,对齐 TS extractToolStats
    /// </summary>
    internal static void ProcessAssistantEntry(
        TranscriptEntry entry,
        Dictionary<string, int> toolCounts,
        ref int assistantMessageCount,
        ref long inputTokens,
        ref long outputTokens,
        ref DateTime? lastAssistantTime,
        ref bool usesMcp,
        ref bool usesWebSearch,
        ref bool usesWebFetch,
        ref bool usesTaskAgent) {
        assistantMessageCount++;
        inputTokens = SafeAddTokens(inputTokens, entry.PromptTokens);
        outputTokens = SafeAddTokens(outputTokens, entry.CompletionTokens);

        if (entry.Timestamp != default) {
            lastAssistantTime = entry.Timestamp;
        }

        // 工具使用统计
        if (!string.IsNullOrEmpty(entry.ToolName)) {
            var toolName = entry.ToolName;
            toolCounts.TryGetValue(toolName, out var count);
            toolCounts[toolName] = count + 1;

            // 检测特殊工具使用
            usesMcp |= toolName.StartsWith("mcp__", StringComparison.OrdinalIgnoreCase);
            usesWebSearch |= string.Equals(toolName, WebToolNameEnumConstants.WebSearch, StringComparison.OrdinalIgnoreCase);
            usesWebFetch |= string.Equals(toolName, WebToolNameEnumConstants.WebFetch, StringComparison.OrdinalIgnoreCase);
            usesTaskAgent |= string.Equals(toolName, AgentToolNameEnumConstants.Agent, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(toolName, "Task", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 处理用户消息条目 — 统计人类消息数/首次提示/时间戳/中断检测 — 纯计算
    /// </summary>
    internal static void ProcessUserEntry(
        TranscriptEntry entry,
        ref int userMessageCount,
        ref string? firstPrompt,
        List<DateTime> userMessageTimestamps,
        ref int userInterruptions) {
        // 仅统计有人类文本的消息（非 tool_result）
        var isHumanMessage = !string.IsNullOrWhiteSpace(entry.Content) &&
            entry.Type != "tool_result";

        if (isHumanMessage) {
            userMessageCount++;
            firstPrompt ??= entry.Content.Length > 200 ? entry.Content[..200] : entry.Content;
        }

        if (isHumanMessage && entry.Timestamp != default) {
            userMessageTimestamps.Add(entry.Timestamp);
        }

        // 检测中断
        if (DetectUserInterruption(entry.Content)) {
            userInterruptions++;
        }
    }

    /// <summary>
    /// 检测用户中断标记 — 纯函数,对齐 TS 中断检测
    /// </summary>
    internal static bool DetectUserInterruption(string content) {
        return content.Contains("[Request interrupted by user", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 处理工具结果条目 — 错误统计/分类/语言与文件统计 — 纯计算
    /// </summary>
    internal static void ProcessToolResultEntry(
        TranscriptEntry entry,
        Dictionary<string, int> toolErrorCategories,
        Dictionary<string, int> languages,
        HashSet<string> modifiedFiles,
        ref int toolErrors,
        ref int gitCommits,
        ref int gitPushes,
        ref int linesAdded,
        ref int linesRemoved) {
        if (entry.Content.Contains("is_error\":true", StringComparison.OrdinalIgnoreCase) ||
            entry.Content.Contains("exit code", StringComparison.OrdinalIgnoreCase)) {
            toolErrors++;
            var category = CategorizeToolError(entry.Content);
            toolErrorCategories.TryGetValue(category, out var catCount);
            toolErrorCategories[category] = catCount + 1;
        }

        // 从工具结果中提取语言和文件信息
        ExtractLanguageAndFileStats(entry, languages, modifiedFiles, ref gitCommits, ref gitPushes, ref linesAdded, ref linesRemoved);
    }

    /// <summary>
    /// 构建 InsightSessionMeta 结果 — 纯计算,集中字段映射
    /// </summary>
    internal static InsightSessionMeta BuildSessionMeta(
        string sessionId,
        DateTime creationTimeUtc,
        double durationMinutes,
        int userMessageCount,
        int assistantMessageCount,
        long inputTokens,
        long outputTokens,
        Dictionary<string, int> toolCounts,
        Dictionary<string, int> languages,
        int gitCommits,
        int gitPushes,
        int linesAdded,
        int linesRemoved,
        HashSet<string> modifiedFiles,
        int userInterruptions,
        int toolErrors,
        Dictionary<string, int> toolErrorCategories,
        bool usesTaskAgent,
        bool usesMcp,
        bool usesWebSearch,
        bool usesWebFetch,
        string? firstPrompt,
        decimal estimatedCost,
        List<DateTime> userMessageTimestamps) {
        return new InsightSessionMeta {
            SessionId = sessionId,
            ProjectPath = string.Empty, // C# 端会话文件不存储项目路径
            StartTime = creationTimeUtc,
            DurationMinutes = SafeRoundDuration(durationMinutes),
            UserMessageCount = userMessageCount,
            AssistantMessageCount = assistantMessageCount,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            ToolCounts = toolCounts,
            Languages = languages,
            GitCommits = gitCommits,
            GitPushes = gitPushes,
            LinesAdded = linesAdded,
            LinesRemoved = linesRemoved,
            FilesModified = modifiedFiles.Count,
            UserInterruptions = userInterruptions,
            ToolErrors = toolErrors,
            ToolErrorCategories = toolErrorCategories,
            UsesTaskAgent = usesTaskAgent,
            UsesMcp = usesMcp,
            UsesWebSearch = usesWebSearch,
            UsesWebFetch = usesWebFetch,
            FirstPrompt = firstPrompt ?? string.Empty,
            EstimatedCostUsd = estimatedCost,
            UserMessageTimestamps = userMessageTimestamps.ToArray(),
        };
    }

    /// <summary>
    /// 读取 JSONL 文件中的所有 TranscriptEntry
    /// </summary>
    private static async Task<List<TranscriptEntry>> ReadEntriesAsync(IFileSystem fs, string filePath, CancellationToken cancellationToken, ILogger? logger = null) {
        var entries = new List<TranscriptEntry>();

        var lines = await fs.ReadAllLinesAsync(filePath, cancellationToken).ConfigureAwait(false);

        foreach (var line in lines) {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try {
                var entry = RelaxedJsonSerializer.Deserialize(line, TranscriptJsonContext.Default.TranscriptEntry);
                if (entry is not null) {
                    entries.Add(entry);
                }
            } catch (JsonException ex) {
                // 跳过格式错误的行
                logger?.LogWarning(ex, "SessionScanner: Skipping malformed JSON line");
            }
        }

        return entries;
    }

    /// <summary>
    /// 分类工具错误 — 对齐 TS extractToolStats 中的错误分类逻辑
    /// </summary>
    internal static string CategorizeToolError(string content) {
        var lower = content.ToLowerInvariant();

        if (lower.Contains("exit code")) return "Command Failed";
        if (lower.Contains("rejected") || lower.Contains("doesn't want")) return "User Rejected";
        if (lower.Contains("string to replace not found") || lower.Contains("no changes")) return "Edit Failed";
        if (lower.Contains("modified since read")) return "File Changed";
        if (lower.Contains("exceeds maximum") || lower.Contains("too large")) return "File Too Large";
        if (lower.Contains("file not found") || lower.Contains("does not exist")) return "File Not Found";

        return "Other";
    }

    /// <summary>
    /// 从工具结果中提取语言和文件统计 — 对齐 TS extractToolStats 中的语言/Git/文件统计
    /// </summary>
    internal static void ExtractLanguageAndFileStats(
        TranscriptEntry entry,
        Dictionary<string, int> languages,
        HashSet<string> modifiedFiles,
        ref int gitCommits,
        ref int gitPushes,
        ref int linesAdded,
        ref int linesRemoved) {
        var content = entry.Content;
        if (string.IsNullOrEmpty(content)) return;

        // 从内容中提取文件路径并识别语言
        foreach (var kvp in ExtensionToLanguage) {
            if (content.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase)) {
                languages.TryGetValue(kvp.Value, out var langCount);
                languages[kvp.Value] = langCount + 1;
            }
        }

        // 检测 Git 操作
        if (content.Contains("git commit", StringComparison.OrdinalIgnoreCase))
            gitCommits++;
        if (content.Contains("git push", StringComparison.OrdinalIgnoreCase))
            gitPushes++;
    }
}