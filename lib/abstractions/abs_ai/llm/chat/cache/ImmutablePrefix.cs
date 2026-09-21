namespace JoinCode.Abstractions.LLM.Chat;

public sealed class ImmutablePrefix {
    /// <summary>获取系统提示词。</summary>
    public string System { get; }
    private readonly Dictionary<string, ToolSpec> _toolSpecs = new(StringComparer.Ordinal);
    private readonly List<string> _toolSpecsOrder = [];
    private readonly ApiMessage[] _fewShots;
    private string? _fingerprintCache;

    /// <summary>获取工具规格列表。</summary>
    public IEnumerable<ToolSpec> ToolSpecs => _toolSpecsOrder.Select(name => _toolSpecs[name]);
    /// <summary>获取 FewShot 示例消息列表。</summary>
    public IEnumerable<ApiMessage> FewShots => _fewShots;

    /// <summary>构造 ImmutablePrefix 实例。</summary>
    public ImmutablePrefix(string system, IEnumerable<ToolSpec> toolSpecs, IEnumerable<ApiMessage> fewShots) {
        System = system ?? throw new ArgumentNullException(nameof(system));
        if (toolSpecs != null) {
            foreach (var t in toolSpecs) {
                if (_toolSpecs.TryAdd(t.Name, t))
                    _toolSpecsOrder.Add(t.Name);
            }
        }
        _fewShots = fewShots != null ? [.. fewShots] : [];
    }

    /// <summary>获取当前前缀的指纹。</summary>
    public string Fingerprint {
        get {
            if (_fingerprintCache is not null) return _fingerprintCache;
            _fingerprintCache = ComputeFingerprint();
            return _fingerprintCache;
        }
    }

    /// <summary>添加或更新工具规格。</summary>
    public void AddTool(ToolSpec tool) {
        ArgumentNullException.ThrowIfNull(tool);
        if (!_toolSpecs.ContainsKey(tool.Name))
            _toolSpecsOrder.Add(tool.Name);
        _toolSpecs[tool.Name] = tool;
        _fingerprintCache = null;
    }

    /// <summary>移除指定名称的工具规格。</summary>
    public void RemoveTool(string toolName) {
        if (_toolSpecs.Remove(toolName)) {
            _toolSpecsOrder.Remove(toolName);
            _fingerprintCache = null;
        }
    }

    /// <summary>校验指纹一致性,返回最新指纹。</summary>
    public string VerifyFingerprint() {
        var fresh = ComputeFingerprint();
        if (_fingerprintCache is not null && _fingerprintCache != fresh) {
            throw new InvalidOperationException(
                $"ImmutablePrefix fingerprint drift: cached={_fingerprintCache}, fresh={fresh}. " +
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
            messages.Add(new ApiMessage(shot.Role, shot.Content));
        }
        return messages;
    }

    private string ComputeFingerprint() {
        var toolSpecsHash = ContentHash.ComputeToolSpecs(_toolSpecsOrder.Select(name => _toolSpecs[name]).ToList());
        var fewShotsBlob = string.Join("|", _fewShots.Select(s => $"{s.Role}:{s.Content}"));
        return ContentHash.Compute($"{System}|{toolSpecsHash}|{fewShotsBlob}");
    }
}
