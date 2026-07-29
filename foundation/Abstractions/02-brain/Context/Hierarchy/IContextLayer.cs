namespace JoinCode.Abstractions.Brain.Context.Hierarchy;

public interface IContextLayer
{
    ContextLayerType LayerType { get; }

    LayerMetadata Metadata { get; }

    string Content { get; set; }

    int TokenCount { get; }

    bool IsCompressed { get; }

    IContextLayer Compress();

    IContextLayer Decompress();

    string GetSummary();
}
