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
            return Task.FromResult(ToolResultBuilder.Error().WithText("模型查找查询不能为空").Build());

        try
        {
            var entries = BuildEntries();
            var engine = new ModelSearchEngine(entries);
            var result = engine.Search(query, max_results ?? 20);

            var response = new StringBuilder();
            response.AppendLine($"[ModelSearch] 查询: {query}");
            response.AppendLine();

            if (result.Lines.Count == 0)
            {
                response.AppendLine("未找到匹配的模型。");
                response.AppendLine($"已注册模型总数: {entries.Count}");
                response.AppendLine("提示: 用 list_groups 查看所有功能分组，再用 map[功能Key] 下钻。");
            }
            else
            {
                if (result.IsGroupList)
                {
                    response.AppendLine("可用功能分组（用 map[功能Key] 下钻查看支持该功能的模型）:");
                    response.AppendLine();
                    foreach (var line in result.Lines)
                        response.AppendLine($"  {line}");
                }
                else if (result.IsModelList)
                {
                    response.AppendLine("匹配模型（格式: vendor/modelId (DisplayName)，用 Agent 工具的 model 参数指定 modelId 创建子代理）:");
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
                response.AppendLine($"匹配 {result.Lines.Count} / {entries.Count} 个模型");
            }

            return Task.FromResult(ToolResultBuilder.Success().WithText(response.ToString()).Build());
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "ModelSearch 查找失败: {Query}", query);
            return Task.FromResult(ToolResultBuilder.Error().WithText($"模型查找失败: {ex.Message}").Build());
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
