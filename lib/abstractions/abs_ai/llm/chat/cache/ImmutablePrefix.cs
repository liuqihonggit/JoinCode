namespace JoinCode.Abstractions.LLM.Chat;

public sealed class ImmutablePrefix {
    /// <summary>获取系统提示词。</summary>
    public string System { get; }
    // P2-⑪ 双索引合并: 原 _toolSpecs(Dictionary) + _toolSpecsOrder(List) 合并为单一有序列表
    private ImmutableList<ToolSpec> _toolSpecs = ImmutableList<ToolSpec>.Empty;
    private readonly ApiMessage[] _fewShots;
    private volatile string? _fingerprintCache;

    /// <summary>获取工具规格列表(按插入顺序)。</summary>
    public IEnumerable<ToolSpec> ToolSpecs => _toolSpecs;
    /// <summary>获取 FewShot 示例消息列表。</summary>
    public IEnumerable<ApiMessage> FewShots => _fewShots;

    /// <summary>构造 ImmutablePrefix 实例。</summary>
    public ImmutablePrefix(string system, IEnumerable<ToolSpec> toolSpecs, IEnumerable<ApiMessage> fewShots) {
        System = system ?? throw new ArgumentNullException(nameof(system));
        if (toolSpecs != null) {
            var list = ImmutableList<ToolSpec>.Empty;
            foreach (var t in toolSpecs) {
                list = AddOrUpdate(list, t);
            }
            _toolSpecs = list;
        }
        _fewShots = fewShots != null ? [.. fewShots] : [];
    }

    /// <summary>获取当前前缀的指纹 — volatile 读缓存,无锁安全。</summary>
    public string Fingerprint {
        get {
            var cached = _fingerprintCache;
            if (cached is not null) return cached;
            var fresh = ComputeFingerprint();
            _fingerprintCache = fresh;
            return fresh;
        }
    }

    /// <summary>添加或更新工具规格 — CAS 原子更新,volatile 失效指纹缓存。</summary>
    public void AddTool(ToolSpec tool) {
        ArgumentNullException.ThrowIfNull(tool);
        ImmutableInterlocked.Update(ref _toolSpecs, static (list, t) => AddOrUpdate(list, t), tool);
        _fingerprintCache = null;
    }

    /// <summary>移除指定名称的工具规格 — CAS 原子更新,volatile 失效指纹缓存。</summary>
    public void RemoveTool(string toolName) {
        ImmutableInterlocked.Update(ref _toolSpecs, static (list, name) => {
            for (var i = 0; i < list.Count; i++) {
                if (list[i].Name == name) return list.RemoveAt(i);
            }
            return list;
        }, toolName);
        _fingerprintCache = null;
    }

    /// <summary>校验指纹一致性,返回最新指纹。</summary>
    public string VerifyFingerprint() {
        var fresh = ComputeFingerprint();
        var cached = _fingerprintCache;
        if (cached is not null && cached != fresh) {
            throw new InvalidOperationException(
                $"ImmutablePrefix fingerprint drift: cached={cached}, fresh={fresh}. " +
                "A mutation path bypassed AddTool's cache invalidation.");
        }
        _fingerprintCache = fresh;
        return fresh;
    }

    /// <summary>转换为消息列表,包含系统消息与 FewShot 示例。</summary>
    public IEnumerable<ApiMessage> ToMessages() {
        var messages = new List<ApiMessage>(_fewShots.Length + 1);
        messages.Add(new ApiMessage(MessageRole.System, System));
        foreach (var shot in _fewShots) {
            messages.Add(shot);
        }
        return messages;
    }

    private string ComputeFingerprint() {
        var toolSpecsHash = ContentHash.ComputeToolSpecs(_toolSpecs);
        var fewShotsBlob = string.Join("|", _fewShots.Select(s => $"{s.Role}:{s.Content}"));
        return ContentHash.Compute($"{System}|{toolSpecsHash}|{fewShotsBlob}");
    }

    private static ImmutableList<ToolSpec> AddOrUpdate(ImmutableList<ToolSpec> list, ToolSpec tool) {
        for (var i = 0; i < list.Count; i++) {
            if (list[i].Name == tool.Name) return list.SetItem(i, tool);
        }
        return list.Add(tool);
    }
}
