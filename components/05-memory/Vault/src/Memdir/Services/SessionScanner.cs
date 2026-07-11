namespace Memdir.Services;

/// <summary>
/// 会话扫描器 — 扫描 ~/.jcc/sessions/ 下所有 .jsonl 会话文件，提取洞察元数据
/// 对齐 TS insights.ts scanAllSessions + logToSessionMeta + extractToolStats
/// </summary>
[Register]
public sealed partial class SessionScanner : IInsightSessionScanner
{
    private readonly string _sessionsDirectory;
    [Inject] private readonly ILogger<SessionScanner>? _logger;
    private readonly IFileSystem _fs;

    /// <summary>文件扩展名到语言名的映射 — 对齐 TS EXTENSION_TO_LANGUAGE</summary>
    private static readonly IReadOnlyDictionary<string, string> ExtensionToLanguage = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "C#",
        [".ts"] = "TypeScript",
        [".tsx"] = "TypeScript",
        [".js"] = "JavaScript",
        [".jsx"] = "JavaScript",
        [".py"] = "Python",
        [".rb"] = "Ruby",
        [".go"] = "Go",
        [".rs"] = "Rust",
        [".java"] = "Java",
        [".md"] = "Markdown",
        [".json"] = "JSON",
        [".yaml"] = "YAML",
        [".yml"] = "YAML",
        [".sh"] = "Shell",
        [".css"] = "CSS",
        [".html"] = "HTML",
        [".ps1"] = "PowerShell",
        [".sql"] = "SQL",
        [".xml"] = "XML",
    };

    public SessionScanner(IFileSystem fs, string? sessionsDirectory = null, ILogger<SessionScanner>? logger = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _sessionsDirectory = sessionsDirectory
            ?? Path.Combine(
                WorkflowConstants.Paths.JccDirectory,
                AppDataConstants.SessionsFolderName);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InsightSessionMeta>> ScanAllSessionsAsync(CancellationToken cancellationToken = default)
    {
        if (!_fs.DirectoryExists(_sessionsDirectory))
        {
            return Array.Empty<InsightSessionMeta>();
        }

        var results = new List<InsightSessionMeta>();

        foreach (var file in _fs.EnumerateFiles(_sessionsDirectory, "*.jsonl", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var meta = await ExtractSessionMetaAsync(file, cancellationToken).ConfigureAwait(false);
                if (meta is not null)
                {
                    results.Add(meta);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "跳过无法读取的会话文件: {File}", file);
            }
        }

        return results;
    }

    /// <summary>
    /// 从单个 JSONL 文件提取 InsightSessionMeta — 对齐 TS logToSessionMeta + extractToolStats
    /// </summary>
    private async Task<InsightSessionMeta?> ExtractSessionMetaAsync(string filePath, CancellationToken cancellationToken)
    {
        var sessionId = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrEmpty(sessionId)) return null;

        var entries = await ReadEntriesAsync(_fs, filePath, cancellationToken).ConfigureAwait(false);
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

        int userMessageCount = 0;
        int assistantMessageCount = 0;
        long inputTokens = 0;
        long outputTokens = 0;
        int gitCommits = 0;
        int gitPushes = 0;
        int linesAdded = 0;
        int linesRemoved = 0;
        int userInterruptions = 0;
        int toolErrors = 0;
        bool usesTaskAgent = false;
        bool usesMcp = false;
        bool usesWebSearch = false;
        bool usesWebFetch = false;
        string? firstPrompt = null;
        decimal estimatedCost = 0;

        foreach (var entry in entries)
        {
            var role = entry.Role;

            // 助手消息统计
            if (string.Equals(role, MessageRoleConstants.Assistant, StringComparison.OrdinalIgnoreCase))
            {
                assistantMessageCount++;
                inputTokens += entry.PromptTokens;
                outputTokens += entry.CompletionTokens;

                if (entry.Timestamp != default)
                {
                    lastAssistantTime = entry.Timestamp;
                }

                // 工具使用统计
                if (!string.IsNullOrEmpty(entry.ToolName))
                {
                    var toolName = entry.ToolName;
                    toolCounts.TryGetValue(toolName, out var count);
                    toolCounts[toolName] = count + 1;

                    // 检测特殊工具使用
                    if (toolName.StartsWith("mcp__", StringComparison.OrdinalIgnoreCase))
                        usesMcp = true;
                    if (string.Equals(toolName, WebToolNameConstants.WebSearch, StringComparison.OrdinalIgnoreCase))
                        usesWebSearch = true;
                    if (string.Equals(toolName, WebToolNameConstants.WebFetch, StringComparison.OrdinalIgnoreCase))
                        usesWebFetch = true;
                    if (string.Equals(toolName, AgentToolNameConstants.Agent, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(toolName, "Task", StringComparison.OrdinalIgnoreCase))
                        usesTaskAgent = true;
                }
            }

            // 用户消息统计
            if (string.Equals(role, MessageRoleConstants.User, StringComparison.OrdinalIgnoreCase))
            {
                // 仅统计有人类文本的消息（非 tool_result）
                var isHumanMessage = !string.IsNullOrWhiteSpace(entry.Content) &&
                    entry.Type != "tool_result";

                if (isHumanMessage)
                {
                    userMessageCount++;
                    firstPrompt ??= entry.Content.Length > 200 ? entry.Content[..200] : entry.Content;

                    if (entry.Timestamp != default)
                    {
                        userMessageTimestamps.Add(entry.Timestamp);
                    }
                }

                // 检测中断
                if (entry.Content.Contains("[Request interrupted by user", StringComparison.OrdinalIgnoreCase))
                {
                    userInterruptions++;
                }
            }

            // 工具结果中的错误统计
            if (string.Equals(role, MessageRoleConstants.Tool, StringComparison.OrdinalIgnoreCase) ||
                entry.Type == "tool_result")
            {
                if (entry.Content.Contains("is_error\":true", StringComparison.OrdinalIgnoreCase) ||
                    entry.Content.Contains("exit code", StringComparison.OrdinalIgnoreCase))
                {
                    toolErrors++;
                    var category = CategorizeToolError(entry.Content);
                    toolErrorCategories.TryGetValue(category, out var catCount);
                    toolErrorCategories[category] = catCount + 1;
                }

                // 从工具结果中提取语言和文件信息
                ExtractLanguageAndFileStats(entry, languages, modifiedFiles, ref gitCommits, ref gitPushes, ref linesAdded, ref linesRemoved);
            }
        }

        return new InsightSessionMeta
        {
            SessionId = sessionId,
            ProjectPath = string.Empty, // C# 端会话文件不存储项目路径
            StartTime = creationTimeUtc,
            DurationMinutes = Math.Round(durationMinutes, 1),
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
    private static async Task<List<TranscriptEntry>> ReadEntriesAsync(IFileSystem fs, string filePath, CancellationToken cancellationToken)
    {
        var entries = new List<TranscriptEntry>();

        var lines = await fs.ReadAllLinesAsync(filePath, cancellationToken).ConfigureAwait(false);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var entry = JsonSerializer.Deserialize(line, TranscriptJsonContext.Default.TranscriptEntry);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }
            catch (JsonException ex)
            {
                // 跳过格式错误的行
                System.Diagnostics.Trace.WriteLine($"SessionScanner: Skipping malformed JSON line: {ex.Message}");
            }
        }

        return entries;
    }

    /// <summary>
    /// 分类工具错误 — 对齐 TS extractToolStats 中的错误分类逻辑
    /// </summary>
    private static string CategorizeToolError(string content)
    {
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
    private static void ExtractLanguageAndFileStats(
        TranscriptEntry entry,
        Dictionary<string, int> languages,
        HashSet<string> modifiedFiles,
        ref int gitCommits,
        ref int gitPushes,
        ref int linesAdded,
        ref int linesRemoved)
    {
        var content = entry.Content;
        if (string.IsNullOrEmpty(content)) return;

        // 从内容中提取文件路径并识别语言
        foreach (var kvp in ExtensionToLanguage)
        {
            if (content.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
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
