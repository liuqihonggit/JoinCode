namespace Core.Context;

/// <summary>
/// 工具规格存储实现 — 管理当前工具规格、延迟工具和已发现工具
/// <para>单一数据源: List&lt;ToolSpec&gt; + List&lt;DeferredToolInfo&gt; + DiscoveredToolSet</para>
/// <para>由 ChatContextManager Actor Consumer 串行调用，方法无需线程安全。</para>
/// </summary>
internal sealed class ToolSpecStore : IToolSpecStore {
    private readonly List<ToolSpec> _currentSpecs = [];
    private readonly List<DeferredToolInfo> _deferredTools = [];
    private readonly DiscoveredToolSet _discoveredTools = new();

    /// <inheritdoc />
    public IReadOnlyList<ToolSpec> CurrentSpecs => _currentSpecs;

    /// <inheritdoc />
    public IReadOnlyList<DeferredToolInfo> DeferredTools => _deferredTools;

    /// <inheritdoc />
    public DiscoveredToolSet DiscoveredTools => _discoveredTools;

    /// <inheritdoc />
    public void UpdateSpecs(IReadOnlyList<ToolSpec> toolSpecs) {
        ArgumentNullException.ThrowIfNull(toolSpecs);

        _currentSpecs.Clear();
        _currentSpecs.AddRange(toolSpecs);

        _deferredTools.Clear();
        foreach (var spec in toolSpecs) {
            var isMcp = spec.Name.Contains('.');
            if (isMcp) {
                _deferredTools.Add(new DeferredToolInfo(spec.Name, spec.Description, spec.InputSchemaJson, isMcp: true, spec.Category, spec.GroupName));
            }
        }
    }

    /// <inheritdoc />
    public async Task SyncDiscoveredToolsFromHistoryAsync(IReadOnlyList<ApiMessage> history) {
        var chatHistory = MessageList.FromList([.. history]);
        var discovered = ToolReferenceExtractor.ExtractDiscoveredToolNames(chatHistory);
        await _discoveredTools.DiscoverRangeAsync(discovered).ConfigureAwait(false);
    }
}
