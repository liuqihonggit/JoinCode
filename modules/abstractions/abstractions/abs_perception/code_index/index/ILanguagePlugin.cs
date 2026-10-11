namespace JoinCode.Abstractions.CodeIndex;

public interface ILanguagePlugin {
    /// <summary>获取语言标识。</summary>
    string LanguageId { get; }
    /// <summary>获取文件扩展名列表。</summary>
    IReadOnlyList<string> FileExtensions { get; }
    /// <summary>提取全部符号和调用信息。</summary>
    ExtractionResult ExtractAll(string sourceCode, string filePath);
    /// <summary>提取符号列表。</summary>
    IReadOnlyList<SymbolInfo> ExtractSymbols(string sourceCode, string filePath);
    /// <summary>提取调用边。</summary>
    IReadOnlyList<CallEdge> ExtractCalls(string sourceCode, string filePath, IReadOnlyList<SymbolInfo> symbols);
    /// <summary>提取依赖边。</summary>
    IReadOnlyList<DependencyEdge> ExtractDependencies(string sourceCode, string filePath, IReadOnlyList<SymbolInfo> symbols);

    /// <summary>异步提取全部符号和调用信息。</summary>
    Task<ExtractionResult> ExtractAllAsync(string sourceCode, string filePath, CancellationToken ct);
    /// <summary>异步提取符号列表。</summary>
    Task<IReadOnlyList<SymbolInfo>> ExtractSymbolsAsync(string sourceCode, string filePath, CancellationToken ct);
}