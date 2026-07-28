namespace JoinCode.Abstractions.LLM.Chat;

public sealed class ImmutablePrefix
{
    public string System { get; }
    private readonly List<ToolSpec> _toolSpecs;
    private readonly ApiMessage[] _fewShots;
    private string? _fingerprintCache;

    public IReadOnlyList<ToolSpec> ToolSpecs => _toolSpecs;
    public IReadOnlyList<ApiMessage> FewShots => _fewShots;

    public ImmutablePrefix(string system, IReadOnlyList<ToolSpec> toolSpecs, IReadOnlyList<ApiMessage> fewShots)
    {
        System = system ?? throw new ArgumentNullException(nameof(system));
        _toolSpecs = toolSpecs != null ? [.. toolSpecs] : [];
        _fewShots = fewShots != null ? [.. fewShots] : [];
    }

    public string Fingerprint
    {
        get
        {
            if (_fingerprintCache is not null) return _fingerprintCache;
            _fingerprintCache = ComputeFingerprint();
            return _fingerprintCache;
        }
    }

    public void AddTool(ToolSpec tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        _toolSpecs.Add(tool);
        _fingerprintCache = null;
    }

    public void RemoveTool(string toolName)
    {
        var index = _toolSpecs.FindIndex(t => t.Name == toolName);
        if (index >= 0)
        {
            _toolSpecs.RemoveAt(index);
            _fingerprintCache = null;
        }
    }

    public string VerifyFingerprint()
    {
        var fresh = ComputeFingerprint();
        if (_fingerprintCache is not null && _fingerprintCache != fresh)
        {
            throw new InvalidOperationException(
                $"ImmutablePrefix fingerprint drift: cached={_fingerprintCache}, fresh={fresh}. " +
                "A mutation path bypassed AddTool's cache invalidation.");
        }
        _fingerprintCache = fresh;
        return fresh;
    }

    public IReadOnlyList<ApiMessage> ToMessages()
    {
        var messages = new List<ApiMessage>(_fewShots.Length + 1);
        messages.Add(new ApiMessage(MessageRole.System, System));
        foreach (var shot in _fewShots)
        {
            messages.Add(new ApiMessage(shot.Role, shot.Content));
        }
        return messages;
    }

    private string ComputeFingerprint()
    {
        var toolSpecsHash = ContentHash.ComputeToolSpecs(_toolSpecs);
        var fewShotsBlob = string.Join("|", _fewShots.Select(s => $"{s.Role}:{s.Content}").ToArray());
        return ContentHash.Compute($"{System}|{toolSpecsHash}|{fewShotsBlob}");
    }
}
