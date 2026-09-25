namespace JoinCode.Abstractions.LLM.Chat;

public sealed class ImmutablePrefix {
    /// <summary>获取系统提示词。</summary>
    public string System { get; }
    private ImmutableDictionary<string, ToolSpec> _toolSpecs = ImmutableDictionary<string, ToolSpec>.Empty;
    private ImmutableList<string> _toolSpecsOrder = ImmutableList<string>.Empty;
    private volatile ImmutableList<ToolSpec> _toolSpecsCache = ImmutableList<ToolSpec>.Empty;
    private readonly ApiMessage[] _fewShots;
    private volatile string? _fingerprintCache;

    /// <summary>获取工具规格列表 — O(1) 直接返回缓存的 ImmutableList 引用，无迭代器创建</summary>
    public IEnumerable<ToolSpec> ToolSpecs => _toolSpecsCache;
    /// <summary>获取 FewShot 示例消息列表。</summary>
    public IEnumerable<ApiMessage> FewShots => _fewShots;

    /// <summary>构造 ImmutablePrefix 实例。</summary>
    public ImmutablePrefix(string system, IEnumerable<ToolSpec> toolSpecs, IEnumerable<ApiMessage> fewShots) {
        System = system ?? throw new ArgumentNullException(nameof(system));
        if (toolSpecs != null) {
            foreach (var t in toolSpecs) {
                if (!_toolSpecs.ContainsKey(t.Name))
                    _toolSpecsOrder = _toolSpecsOrder.Add(t.Name);
                _toolSpecs = _toolSpecs.SetItem(t.Name, t);
            }
        }
        _toolSpecsCache = BuildToolSpecsCache();
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

    /// <summary>添加或更新工具规格 — CAS 原子更新工具字典+顺序列表,volatile 失效指纹缓存。</summary>
    public void AddTool(ToolSpec tool) {
        ArgumentNullException.ThrowIfNull(tool);
        var wasNew = !_toolSpecs.ContainsKey(tool.Name);
        if (wasNew)
            ImmutableInterlocked.Update(ref _toolSpecsOrder, static (list, name) => list.Add(name), tool.Name);
        ImmutableInterlocked.Update(ref _toolSpecs, static (dict, t) => dict.SetItem(t.Name, t), tool);
        _toolSpecsCache = BuildToolSpecsCache();
        _fingerprintCache = null;
    }

    /// <summary>移除指定名称的工具规格 — CAS 原子更新,volatile 失效指纹缓存。</summary>
    public void RemoveTool(string toolName) {
        if (_toolSpecs.ContainsKey(toolName)) {
            ImmutableInterlocked.Update(ref _toolSpecs, static (dict, name) => dict.Remove(name), toolName);
            ImmutableInterlocked.Update(ref _toolSpecsOrder, static (list, name) => list.Remove(name), toolName);
            _toolSpecsCache = BuildToolSpecsCache();
            _fingerprintCache = null;
        }
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
        var specs = _toolSpecs;
        var order = _toolSpecsOrder;
        var toolSpecsHash = ContentHash.ComputeToolSpecs(order.Select(name => specs[name]));
        var fewShotsBlob = string.Join("|", _fewShots.Select(s => $"{s.Role}:{s.Content}"));
        return ContentHash.Compute($"{System}|{toolSpecsHash}|{fewShotsBlob}");
    }

    private ImmutableList<ToolSpec> BuildToolSpecsCache() {
        var specs = _toolSpecs;
        var order = _toolSpecsOrder;
        var builder = ImmutableList.CreateBuilder<ToolSpec>();
        foreach (var name in order) {
            builder.Add(specs[name]);
        }
        return builder.ToImmutable();
    }
}
