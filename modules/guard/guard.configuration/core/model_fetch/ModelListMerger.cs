namespace Core.Configuration.ModelFetch;

/// <summary>
/// 模型列表智能合并器 — 以远程 id 列表为准增删，保留本地已有模型的元数据
/// </summary>
public static class ModelListMerger {
    /// <summary>
    /// 智能合并本地模型列表与远程模型信息
    /// <para>远程有本地无 → 新增（用远程元数据完整填充: Description/ContextWindow/Modalities）</para>
    /// <para>远程无本地有 → 保留（用户手动配置的模型不应被远程列表删除）</para>
    /// <para>远程有本地有 → 保留本地元数据，仅补全缺失字段（ContextWindow==0 或 Description=="" 时用远程补）</para>
    /// </summary>
    public static List<ModelItemConfig> Merge(List<ModelItemConfig>? localModels, IReadOnlyList<RemoteModelInfo> remoteModels) {
        if (remoteModels.Count == 0)
            return localModels ?? [];

        var localById = new Dictionary<string, ModelItemConfig>(StringComparer.OrdinalIgnoreCase);
        if (localModels is not null) {
            foreach (var m in localModels) {
                if (!string.IsNullOrEmpty(m.Id))
                    localById[m.Id] = m;
            }
        }

        var result = new List<ModelItemConfig>(remoteModels.Count + localById.Count);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var remote in remoteModels) {
            if (string.IsNullOrEmpty(remote.Id))
                continue;
            seenIds.Add(remote.Id);
            if (localById.TryGetValue(remote.Id, out var local))
                result.Add(SupplementLocal(local, remote));
            else
                result.Add(BuildFromRemote(remote));
        }
        foreach (var (id, local) in localById) {
            if (!seenIds.Contains(id))
                result.Add(local);
        }
        return result;
    }

    /// <summary>
    /// 用远程元数据补全本地缺失字段 — ContextWindow==0 用远程补，Description=="" 用远程补
    /// <para>其余字段（Capabilities/DisplayName/Aliases 等）保留用户手动值，不被覆盖</para>
    /// </summary>
    private static ModelItemConfig SupplementLocal(ModelItemConfig local, RemoteModelInfo remote) {
        var contextWindow = local.ContextWindow == 0 && remote.ContextLength > 0
            ? remote.ContextLength
            : local.ContextWindow;
        var description = string.IsNullOrEmpty(local.Description) && !string.IsNullOrEmpty(remote.Description)
            ? remote.Description
            : local.Description;
        return local with { ContextWindow = contextWindow, Description = description };
    }

    /// <summary>
    /// 从远程模型信息构建本地配置 — 新增模型，用远程元数据填充 Description/ContextWindow
    /// <para>Capabilities 留默认（模态由用户在 settings.json 显式配置，不从 API 或 ID 推断）</para>
    /// </summary>
    private static ModelItemConfig BuildFromRemote(RemoteModelInfo remote) {
        return new ModelItemConfig {
            Id = remote.Id,
            CanonicalId = remote.Id,
            DisplayName = GenerateDisplayName(remote.Id),
            ContextWindow = remote.ContextLength,
            Description = remote.Description,
            Capabilities = new ModelCapabilitiesConfig(),
        };
    }

    /// <summary>
    /// 从 model id 生成默认显示名称 — 将连字符/下划线替换为空格并首字母大写
    /// </summary>
    private static string GenerateDisplayName(string modelId) {
        var parts = modelId.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return modelId;

        return string.Join(' ', parts.Select(CapitalizeFirst));
    }

    private static string CapitalizeFirst(string s) {
        if (s.Length == 0) return s;
        return char.ToUpperInvariant(s[0]) + s[1..];
    }
}