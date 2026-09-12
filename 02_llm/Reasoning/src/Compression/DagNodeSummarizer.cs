namespace JoinCode.Reasoning.Compression;

/// <summary>
/// DAG 节点摘要器 — 对已裁决/驳回的节点生成摘要替代原文，释放 token 占用
/// 保留原始 Content 在 OriginalContent 字段中（可追溯）
/// </summary>
public sealed class DagNodeSummarizer
{
    private readonly IContextCompressor? _compressor;

    public DagNodeSummarizer(IContextCompressor? compressor = null)
    {
        _compressor = compressor;
    }

    /// <summary>
    /// 对 DAG 中已裁决的节点，将 Content 替换为摘要
    /// </summary>
    public async Task SummarizeResolvedNodesAsync(Dag<ReasoningPayload> dag, int threshold = 30, CancellationToken ct = default)
    {
        if (dag.Nodes.Count < threshold) return;

        var resolvedNodes = dag.Nodes.Values
            .Where(n => n.Payload.State is DataState.Fact or DataState.Rejected)
            .Where(n => n.Payload.Content.Length > 100)
            .Where(n => n.Payload.OriginalContent is null)
            .ToList();

        foreach (var node in resolvedNodes)
        {
            if (_compressor is not null && _compressor.CanCompress(node.Payload.Content, ContentType.Text))
            {
                var result = await _compressor.CompressAsync(
                    node.Payload.Content, ContentType.Text,
                    CompressionOptions.Aggressive, ct).ConfigureAwait(false);

                if (result.IsSuccess && result.CompressionRatio < 0.7)
                {
                    node.Payload.OriginalContent = node.Payload.Content;
                    node.Payload.Content = result.CompressedContent;
                }
            }
            else
            {
                node.Payload.OriginalContent = node.Payload.Content;
                node.Payload.Content = node.Payload.Content[..100] + "...";
            }
        }
    }
}
