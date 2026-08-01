namespace JoinCode.Abstractions.Interfaces.Doctor;

/// <summary>
/// 源码工程引擎 — Doctor 模式的源码工程能力核心
/// 解决 "拿着 exe 无法自举" 的根本问题
/// </summary>
public interface ISourceCodeEngine
{
    /// <summary>
    /// 定位源码仓库根目录
    /// 从 exe 所在目录向上搜索 .git，或从环境变量/配置获取
    /// </summary>
    Task<SourceCodeLocation> LocateSourceRepositoryAsync(
        string? hintPath = null,
        CancellationToken ct = default);

    /// <summary>
    /// 在指定目录中编译整个项目（七层 slnx 依赖顺序）
    /// </summary>
    Task<FullBuildResult> BuildFullProjectAsync(
        string worktreePath,
        string configuration = "Debug",
        CancellationToken ct = default);

    /// <summary>
    /// 获取编译产物 exe 路径
    /// </summary>
    Task<string> GetArtifactExePathAsync(
        string worktreePath,
        string configuration = "Debug",
        CancellationToken ct = default);

    /// <summary>
    /// 替换运行中的 exe 为新编译的版本
    /// </summary>
    Task<ExeSwapResult> SwapExeAsync(
        string currentExePath,
        string newExePath,
        string patientId,
        CancellationToken ct = default);

    /// <summary>
    /// 确保源码可用 — 三策略: JCC_SOURCE_DIR → exe目录搜索.git → git clone
    /// </summary>
    Task<SourceCodeLocation> EnsureSourceAvailableAsync(
        string? repoUrl = null,
        CancellationToken ct = default);
}
