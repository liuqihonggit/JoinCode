namespace JoinCode.Abstractions.Interfaces;

public interface ICodeService {
    Task<string> GenerateCodeAsync(string prompt, CancellationToken cancellationToken = default);
    Task<string> AnalyzeCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<string> ExecuteCodeAsync(string code, CancellationToken cancellationToken = default);
}
