namespace JoinCode.Abstractions.Interfaces;

public interface ICodeService {
    /// <summary>异步生成代码。</summary>
    Task<string> GenerateCodeAsync(string prompt, CancellationToken cancellationToken = default);
    /// <summary>异步分析代码。</summary>
    Task<string> AnalyzeCodeAsync(string code, CancellationToken cancellationToken = default);
    /// <summary>异步执行代码。</summary>
    Task<string> ExecuteCodeAsync(string code, CancellationToken cancellationToken = default);
}