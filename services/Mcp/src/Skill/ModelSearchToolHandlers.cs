namespace McpToolDispatch;

/// <summary>
/// 模型查找工具处理器 — 按功能→型号渐进式展开模型表。
/// <para>Kind=OnError：不在首次提示词暴露，模态不匹配报错时动态注入。</para>
/// <para>语法对齐 ToolSearch：list_groups / map[功能Key] / map[功能Key][vendor] / 关键词</para>
/// <para>数据源 IModelConfigLoader.Config.Providers → ModelSearchEntry 列表</para>
/// </summary>
[McpToolDispatch(SystemToolNameConstants.ModelSearch, Kind = ToolKind.OnError)]
public partial class ModelSearchToolHandlers
{
    private readonly IModelConfigLoader _modelConfigLoader;
    [Inject] private readonly ILogger<ModelSearchToolHandlers>? _logger;

    public ModelSearchToolHandlers(IModelConfigLoader modelConfigLoader, ILogger<ModelSearchToolHandlers>? logger = null)
    {
        _modelConfigLoader = modelConfigLoader ?? throw new ArgumentNullException(nameof(modelConfigLoader));
        _logger = logger;
    }

    [McpTool(SystemToolNameConstants.ModelSearch, "按功能→型号渐进式查找模型表，用于模态不匹配时寻找支持目标功能的模型", "system")]
    public Task<ToolResult> SearchModelsAsync(
        [McpToolParameter("查找查询：'list_groups' 列出功能分组；'map[功能Key]' 列出支持该功能的模型（如 map[generateImage]）；'map[功能Key][vendor]' 按 vendor 过滤；关键词模糊搜索模型名/显示名")] string query,
        [McpToolParameter("最大结果数（可选，默认 20）", Required = false)] int? max_results = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult(ToolResultBuilder.Error().WithText(L.T(StringKey.ModelSearchQueryCannotBeEmpty)).Build());

        try
        {
            var entries = BuildEntries();
            var engine = new ModelSearchEngine(entries);
            var result = engine.Search(query, max_results ?? 20);

            var response = new StringBuilder();
            response.AppendLine(L.T(StringKey.ModelSearchResultTitle, query));
            response.AppendLine();

            if (result.Lines.Count == 0)
            {
                response.AppendLine(L.T(StringKey.ModelSearchNoMatch));
                response.AppendLine(L.T(StringKey.ModelSearchRegisteredCount, entries.Count));
                response.AppendLine(L.T(StringKey.ModelSearchHint));
            }
            else
            {
                if (result.IsGroupList)
                {
                    response.AppendLine(L.T(StringKey.ModelSearchGroupListHeader));
                    response.AppendLine();
                    foreach (var line in result.Lines)
                        response.AppendLine($"  {line}");
                }
                else if (result.IsModelList)
                {
                    response.AppendLine(L.T(StringKey.ModelSearchModelListHeader));
                    response.AppendLine();
                    foreach (var line in result.Lines)
                        response.AppendLine($"  {line}");
                }
                else
                {
                    foreach (var line in result.Lines)
                        response.AppendLine($"  {line}");
                }

                response.AppendLine();
                response.AppendLine(L.T(StringKey.ModelSearchMatchedCount, result.Lines.Count, entries.Count));
            }

            return Task.FromResult(ToolResultBuilder.Success().WithText(response.ToString()).Build());
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.ModelSearchFailedLog), query);
            return Task.FromResult(ToolResultBuilder.Error().WithText(L.T(StringKey.ModelSearchFailed, ex.Message)).Build());
        }
    }

    private List<ModelSearchEntry> BuildEntries()
    {
        var entries = new List<ModelSearchEntry>();
        foreach (var provider in _modelConfigLoader.Config.Providers)
        {
            foreach (var model in provider.Value.Models)
            {
                entries.Add(new ModelSearchEntry(
                    provider.Key,
                    model.Id,
                    model.DisplayName,
                    model.Capabilities.Modalities));
            }
        }
        return entries;
    }
}
