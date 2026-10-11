namespace JoinCode.Abstractions.Brain.Context.Hierarchy;

public interface IContextLayer {
    /// <summary>获取层级类型。</summary>
    ContextLayerType LayerType { get; }

    /// <summary>获取层级元数据。</summary>
    LayerMetadata Metadata { get; }

    /// <summary>获取或设置内容。</summary>
    string Content { get; set; }

    /// <summary>获取令牌数。</summary>
    int TokenCount { get; }

    /// <summary>获取是否已压缩。</summary>
    bool IsCompressed { get; }

    /// <summary>压缩当前层级。</summary>
    IContextLayer Compress();

    /// <summary>解压当前层级。</summary>
    IContextLayer Decompress();

    /// <summary>获取摘要。</summary>
    string GetSummary();
}