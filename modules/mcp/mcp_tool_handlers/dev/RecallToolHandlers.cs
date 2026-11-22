namespace McpToolDispatch;

/// <summary>
/// 笔记本回忆工具处理器 — LLM 通过 recall 工具查阅被折叠/压缩的原始对话内容
/// <para>折叠/压缩时自动存档原始消息到磁盘，LLM 可按需用 recall_list/recall_read/recall_search 查阅</para>
/// </summary>
[McpToolDispatch(ToolCategory.Notebook, Optional = true)]
public partial class RecallToolHandlers {
    private readonly JoinCode.Abstractions.LLM.Chat.IContextArchiveService _notebookService;
    private readonly ILogger<RecallToolHandlers>? _logger;

    /// <summary>
    /// 初始化 <see cref="RecallToolHandlers"/> 实例
    /// </summary>
    /// <param name="notebookService">笔记本服务</param>
    /// <param name="logger">日志记录器（可选）</param>
    public RecallToolHandlers(
        JoinCode.Abstractions.LLM.Chat.IContextArchiveService notebookService,
        ILogger<RecallToolHandlers>? logger = null) {
        _notebookService = notebookService ?? throw new ArgumentNullException(nameof(notebookService));
        _logger = logger;
    }

    /// <summary>
    /// 列出当前会话的所有存档条目（折叠/压缩时自动创建的存档）
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>存档条目列表</returns>
    [McpTool(SystemToolNameEnumConstants.RecallList, "List folded conversation archives that can be recalled", "notebook")]
    public async Task<ToolResult> ListArchivesAsync(CancellationToken cancellationToken = default) {
        try {
            var entries = await _notebookService.ListArchivesAsync("default", cancellationToken).ConfigureAwait(false);

            if (entries.Count == 0) {
                return ToolResultBuilder.Success().WithText("No folded archives found. Archives are created automatically when conversation history is folded or compacted.").Build();
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Found {entries.Count} folded archive(s):");
            sb.AppendLine();

            foreach (var entry in entries) {
                sb.AppendLine($"  [{entry.FoldId}]");
                sb.AppendLine($"    Time: {entry.Timestamp:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"    Messages: {entry.MessageCount}");
                sb.AppendLine($"    Summary: {entry.SummaryPreview}");
                sb.AppendLine();
            }

            sb.AppendLine("Use recall_read with a fold_id to read the full original content of any archive.");

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        } catch (OperationCanceledException) { throw; } catch (Exception ex) {
            _logger?.LogError(ex, "recall_list failed");
            return ToolResultBuilder.Error().WithText($"recall_list failed: {ex.Message}").Build();
        }
    }

    /// <summary>
    /// 读取指定存档的完整原始内容（被折叠/压缩前的原始消息）
    /// </summary>
    /// <param name="fold_id">存档唯一标识（从 recall_list 获取）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>存档的完整 JSON 内容</returns>
    [McpTool(SystemToolNameEnumConstants.RecallRead, "Read full original content of a folded archive by fold_id", "notebook")]
    public async Task<ToolResult> ReadArchiveAsync(
        [McpToolParameter("The fold_id of the archive to read (from recall_list)", Required = true)] string fold_id,
        CancellationToken cancellationToken = default) {
        try {
            if (string.IsNullOrWhiteSpace(fold_id)) {
                return ToolResultBuilder.Error().WithText("fold_id is required").Build();
            }

            var content = await _notebookService.ReadArchiveAsync("default", fold_id, cancellationToken).ConfigureAwait(false);

            if (content is null) {
                return ToolResultBuilder.Error().WithText($"Archive '{fold_id}' not found. Use recall_list to see available archives.").Build();
            }

            return ToolResultBuilder.Success().WithText(content).Build();
        } catch (OperationCanceledException) { throw; } catch (Exception ex) {
            _logger?.LogError(ex, "recall_read failed for {FoldId}", fold_id);
            return ToolResultBuilder.Error().WithText($"recall_read failed: {ex.Message}").Build();
        }
    }

    /// <summary>
    /// 搜索存档内容（匹配摘要文本中的关键词）
    /// </summary>
    /// <param name="keyword">搜索关键词</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>匹配的存档条目列表</returns>
    [McpTool(SystemToolNameEnumConstants.RecallSearch, "Search folded archives by keyword in summary text", "notebook")]
    public async Task<ToolResult> SearchArchivesAsync(
        [McpToolParameter("Keyword to search in archive summaries", Required = true)] string keyword,
        CancellationToken cancellationToken = default) {
        try {
            if (string.IsNullOrWhiteSpace(keyword)) {
                return ToolResultBuilder.Error().WithText("keyword is required").Build();
            }

            var entries = await _notebookService.SearchArchivesAsync("default", keyword, cancellationToken).ConfigureAwait(false);

            if (entries.Count == 0) {
                return ToolResultBuilder.Success().WithText($"No archives found matching '{keyword}'.").Build();
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Found {entries.Count} archive(s) matching '{keyword}':");
            sb.AppendLine();

            foreach (var entry in entries) {
                sb.AppendLine($"  [{entry.FoldId}]");
                sb.AppendLine($"    Time: {entry.Timestamp:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"    Messages: {entry.MessageCount}");
                sb.AppendLine($"    Summary: {entry.SummaryPreview}");
                sb.AppendLine();
            }

            sb.AppendLine("Use recall_read with a fold_id to read the full original content.");

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        } catch (OperationCanceledException) { throw; } catch (Exception ex) {
            _logger?.LogError(ex, "recall_search failed for keyword {Keyword}", keyword);
            return ToolResultBuilder.Error().WithText($"recall_search failed: {ex.Message}").Build();
        }
    }
}
