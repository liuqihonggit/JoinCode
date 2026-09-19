

namespace McpToolDispatch;

/// <summary>
/// 技能搜索处理器 — 提供技能搜索、推荐和发现功能
/// </summary>
[McpToolDispatch(ToolCategory.Skill, Optional = true)]
public sealed partial class SkillSearchToolHandlers {
    private readonly JoinCode.Abstractions.Interfaces.ISkillSearchService _searchService;
    private readonly ILogger<SkillSearchToolHandlers>? _logger;

    /// <summary>
    /// 初始化 <see cref="SkillSearchToolHandlers"/> 实例
    /// </summary>
    /// <param name="searchService">技能搜索服务</param>
    /// <param name="logger">日志记录器（可选）</param>
    public SkillSearchToolHandlers(JoinCode.Abstractions.Interfaces.ISkillSearchService searchService, ILogger<SkillSearchToolHandlers>? logger = null) {
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _logger = logger;
    }

    /// <summary>
    /// 搜索可用技能
    /// </summary>
    /// <param name="keyword">搜索关键词（可选）</param>
    /// <param name="tags">标签过滤（逗号分隔）</param>
    /// <param name="category">分类过滤</param>
    /// <param name="max_results">最大结果数（默认 10）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果</returns>
    [McpTool(SkillToolNameEnumConstants.SkillSearch, "Search available skills", "skill")]
    public async Task<ToolResult> SkillSearchAsync(
        [McpToolParameter("Search keyword", Required = false)] string? keyword = null,
        [McpToolParameter("Tag filter (comma-separated)", Required = false)] string? tags = null,
        [McpToolParameter("Category filter", Required = false)] string? category = null,
        [McpToolParameter("Maximum number of results", Required = false, DefaultValue = "10")] int max_results = 10,
        CancellationToken cancellationToken = default) {
        try {
            var query = new JoinCode.Abstractions.Models.SkillSearch.SkillSearchQuery {
                Keyword = keyword,
                Tags = string.IsNullOrEmpty(tags) ? Array.Empty<string>() : tags.Split(','),
                Category = category,
                PageSize = max_results
            };

            var results = await _searchService.SearchAsync(query, cancellationToken).ConfigureAwait(false);

            var response = new StringBuilder(512);
            response.AppendLine(L.T(StringKey.SkillSearchResult, results.Count));

            if (results.Count == 0) {
                response.AppendLine(L.T(StringKey.NoMatchingSkillFound));
            } else {
                foreach (var result in results) {
                    response.AppendLine($"  {result.SkillName} ({L.T(StringKey.LabelRelevance, result.RelevanceScore.ToString("P0"))}) - {result.Description}");

                    if (result.Tags.Count > 0) {
                        response.AppendLine($"    {L.T(StringKey.LabelTags, string.Join(", ", result.Tags))}");
                    }
                }
            }

            return ToolResultBuilder.Success().WithText(response.ToString()).Build();
        } catch (Exception ex) {
            _logger?.LogError(ex, L.T(StringKey.SkillSearchFailedLog));
            return ToolResultBuilder.Error()
                .WithText(L.T(StringKey.SkillSearchFailed, ex.Message))
                .Build();
        }
    }

    /// <summary>
    /// 基于上下文推荐技能
    /// </summary>
    /// <param name="context">上下文描述</param>
    /// <param name="max_results">最大结果数（默认 5）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果</returns>
    [McpTool(SkillToolNameEnumConstants.SkillRecommend, "Recommend skills based on context", "skill")]
    public async Task<ToolResult> SkillRecommendAsync(
        [McpToolParameter("Context description")] string context,
        [McpToolParameter("Maximum number of results", Required = false, DefaultValue = "5")] int max_results = 5,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(context)) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.ContextDescriptionCannotBeEmpty)).Build();
        }

        try {
            var results = await _searchService.RecommendAsync(context, max_results, cancellationToken).ConfigureAwait(false);

            var response = new StringBuilder(512);
            response.AppendLine(L.T(StringKey.SkillRecommendResult, results.Count));

            if (results.Count == 0) {
                response.AppendLine(L.T(StringKey.NoRecommendedSkillFound));
            } else {
                foreach (var result in results) {
                    response.AppendLine($"  {result.SkillName} ({L.T(StringKey.LabelRelevance, result.RelevanceScore.ToString("P0"))}) - {result.Description}");

                    if (result.Tags.Count > 0) {
                        response.AppendLine($"    {L.T(StringKey.LabelTags, string.Join(", ", result.Tags))}");
                    }
                }
            }

            return ToolResultBuilder.Success().WithText(response.ToString()).Build();
        } catch (Exception ex) {
            _logger?.LogError(ex, L.T(StringKey.SkillRecommendFailedLog));
            return ToolResultBuilder.Error()
                .WithText(L.T(StringKey.SkillRecommendFailed, ex.Message))
                .Build();
        }
    }

    /// <summary>
    /// 技能发现 — 对齐 TS DiscoverSkillsTool
    /// 基于用户输入/上下文自动发现相关技能
    /// TS 版为内部实验功能(开源stub), C# 版使用本地 SkillSearchService 实现
    /// </summary>
    [McpTool(SkillToolNameEnumConstants.DiscoverSkills, "Discover relevant skills based on user input or context", "skill")]
    public async Task<ToolResult> DiscoverSkillsAsync(
        [McpToolParameter("User input or context to discover skills for")] string context,
        [McpToolParameter("Maximum number of results", Required = false, DefaultValue = "5")] int max_results = 5,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(context)) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.ContextDescriptionCannotBeEmpty)).Build();
        }

        try {
            // 对齐 TS: 先用关键词搜索，再用上下文推荐，合并去重
            var keywordResults = await _searchService.SearchAsync(
                new JoinCode.Abstractions.Models.SkillSearch.SkillSearchQuery { Keyword = context, PageSize = max_results },
                cancellationToken).ConfigureAwait(false);

            var recommendResults = await _searchService.RecommendAsync(
                context, max_results, cancellationToken).ConfigureAwait(false);

            // 合并去重（按 SkillName）
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var combined = new List<JoinCode.Abstractions.Models.SkillSearch.SkillSearchResult>();

            foreach (var r in keywordResults.Concat(recommendResults)) {
                if (seen.Add(r.SkillName)) {
                    combined.Add(r);
                }
            }

            // 按相关度排序，取前 max_results
            var results = combined
                .OrderByDescending(r => r.RelevanceScore)
                .Take(max_results)
                .ToList();

            var response = new StringBuilder(512);
            response.AppendLine($"Discovered {results.Count} relevant skill(s) for: {context}");

            if (results.Count == 0) {
                response.AppendLine("No relevant skills found. Use skill_list to see all available skills.");
            } else {
                foreach (var result in results) {
                    response.AppendLine($"  {result.SkillName} - {result.Description}");

                    if (result.Tags.Count > 0) {
                        response.AppendLine($"    Tags: {string.Join(", ", result.Tags)}");
                    }
                }
            }

            return ToolResultBuilder.Success().WithText(response.ToString()).Build();
        } catch (Exception ex) {
            _logger?.LogError(ex, "Skill discovery failed: {Message}", ex.Message);
            return ToolResultBuilder.Error()
                .WithText($"Skill discovery failed: {ex.Message}")
                .Build();
        }
    }
}