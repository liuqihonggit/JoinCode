namespace JoinCode.Abstractions.CodeIndex;

public interface ILanguagePlugin
{
    string LanguageId { get; }
    IReadOnlyList<string> FileExtensions { get; }
    ExtractionResult ExtractAll(string sourceCode, string filePath);
    IReadOnlyList<SymbolInfo> ExtractSymbols(string sourceCode, string filePath);
    IReadOnlyList<CallEdge> ExtractCalls(string sourceCode, string filePath, IReadOnlyList<SymbolInfo> symbols);
    IReadOnlyList<DependencyEdge> ExtractDependencies(string sourceCode, string filePath, IReadOnlyList<SymbolInfo> symbols);

    Task<ExtractionResult> ExtractAllAsync(string sourceCode, string filePath, CancellationToken ct);
    Task<IReadOnlyList<SymbolInfo>> ExtractSymbolsAsync(string sourceCode, string filePath, CancellationToken ct);
}
