namespace JoinCode.Abstractions.Interfaces.Doctor;

/// <summary>
/// LLM 驱动的源码 patch 生成器 — 分析问题 → 生成源码修改
/// </summary>
public interface ICodePatchGenerator
{
    /// <summary>
    /// 根据诊断报告和源码上下文，生成修复 patch
    /// </summary>
    Task<CodePatch> GeneratePatchAsync(
        DiagnosticReport diagnostic,
        SourceCodeContext sourceContext,
        IReadOnlyList<CodePatch>? historicalPatches = null,
        CancellationToken ct = default);
}
