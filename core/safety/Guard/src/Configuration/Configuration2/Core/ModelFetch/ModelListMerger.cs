namespace Core.Configuration.ModelFetch;

/// <summary>
/// 模型列表智能合并器 — 以远程 id 列表为准增删，保留本地已有模型的元数据
/// </summary>
public static class ModelListMerger
{
    /// <summary>
    /// 智能合并本地模型列表与远程 id 列表
    /// <para>远程有本地无 → 新增（用 id 生成默认 DisplayName，Capabilities 留默认）</para>
    /// <para>远程无本地有 → 删除</para>
    /// <para>远程有本地有 → 保留本地元数据（Capabilities/DisplayName/ContextWindow 等）</para>
    /// </summary>
    public static List<ModelItemConfig> Merge(List<ModelItemConfig>? localModels, IReadOnlyList<string> remoteIds)
    {
        if (remoteIds.Count == 0)
            return localModels ?? [];

        var remoteSet = new HashSet<string>(remoteIds, StringComparer.OrdinalIgnoreCase);
        var localById = new Dictionary<string, ModelItemConfig>(StringComparer.OrdinalIgnoreCase);
        if (localModels is not null)
        {
            foreach (var m in localModels)
            {
                if (!string.IsNullOrEmpty(m.Id))
                    localById[m.Id] = m;
            }
        }

        var result = new List<ModelItemConfig>(remoteIds.Count);
        foreach (var remoteId in remoteIds)
        {
            if (localById.TryGetValue(remoteId, out var local))
            {
                result.Add(local);
            }
            else
            {
                result.Add(new ModelItemConfig
                {
                    Id = remoteId,
                    CanonicalId = remoteId,
                    DisplayName = GenerateDisplayName(remoteId),
                    Capabilities = InferCapabilities(remoteId),
                });
            }
        }
        return result;
    }

    /// <summary>
    /// 从 model id 生成默认显示名称 — 如 "gpt-4o-mini" → "Gpt 4o Mini"
    /// </summary>
    private static string GenerateDisplayName(string modelId)
    {
        var parts = modelId.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return modelId;

        return string.Join(' ', parts.Select(CapitalizeFirst));
    }

    private static string CapitalizeFirst(string s)
    {
        if (s.Length == 0) return s;
        return char.ToUpperInvariant(s[0]) + s[1..];
    }

    /// <summary>
    /// 从模型 ID 推断模态能力 — API /models 端点只返回 ID，需根据命名约定推断
    /// <para>"vision" → ReadImage | ReadGif（视觉模型可读图片和动图）</para>
    /// <para>"pro"/"reasoner"/"r1" → Thinking（推理模型）</para>
    /// <para>"flash" → FastMode（快速模型）</para>
    /// <para>所有模型基础: Text | ToolUse</para>
    /// </summary>
    public static ModelCapabilitiesConfig InferCapabilities(string modelId)
    {
        var id = modelId.AsSpan();
        var modalities = ModelModalityKind.Text | ModelModalityKind.ToolUse;
        var thinkingMode = false;

        if (id.Contains("vision", StringComparison.OrdinalIgnoreCase))
            modalities |= ModelModalityKind.ReadImage | ModelModalityKind.ReadGif;

        if (id.Contains("pro", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("reasoner", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("r1", StringComparison.OrdinalIgnoreCase))
        {
            modalities |= ModelModalityKind.Thinking;
            thinkingMode = true;
        }

        return new ModelCapabilitiesConfig
        {
            FastMode = id.Contains("flash", StringComparison.OrdinalIgnoreCase),
            ThinkingMode = thinkingMode,
            Modalities = modalities,
        };
    }
}
