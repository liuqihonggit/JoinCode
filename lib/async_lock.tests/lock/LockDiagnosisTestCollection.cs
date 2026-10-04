namespace LockDiagnosis.Tests;

/// <summary>
/// LockDiagnosis 测试集合 — 禁止并行执行,避免全局 LockRegistry.DiagnosticsEnabled 竞态(flaky 根因)。
/// </summary>
[CollectionDefinition("lock-diagnosis")]
public class LockDiagnosisTestCollection { }
