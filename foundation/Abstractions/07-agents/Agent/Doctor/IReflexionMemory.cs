namespace JoinCode.Abstractions.Interfaces.Doctor;

/// <summary>
/// 反思记忆存储 — 持久化修复经验
/// </summary>
public interface IReflexionMemory
{
    /// <summary>
    /// 存储修复经验
    /// </summary>
    Task StoreAsync(
        CodePatch patch,
        DiagnosticReport diagnostic,
        bool wasSuccessful,
        CancellationToken ct = default);

    /// <summary>
    /// 检索与当前诊断相似的历史修复
    /// </summary>
    Task<IReadOnlyList<CodePatch>> RetrieveSimilarPatchesAsync(
        DiagnosticReport diagnostic,
        int maxResults = 3,
        CancellationToken ct = default);
}
