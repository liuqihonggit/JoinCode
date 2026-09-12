namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 调用点查找器 — 队长改热文件时找所有调用点，秘书执行连带改
/// 查找逻辑通过回调注入（实际接入时绑定 CodeSemanticSearch/grep）
/// </summary>
public interface ICallSiteFinder
{
    /// <summary>
    /// 查找某符号的所有调用点
    /// </summary>
    /// <param name="symbolName">符号名（接口名/方法名/枚举名等）</param>
    /// <param name="searchRoot">搜索根目录</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>调用点列表</returns>
    Task<IReadOnlyList<CodeCallSite>> FindCallSitesAsync(string symbolName, string searchRoot, CancellationToken cancellationToken = default);
}
