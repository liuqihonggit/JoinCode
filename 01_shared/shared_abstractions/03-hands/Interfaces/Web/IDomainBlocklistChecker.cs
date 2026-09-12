namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 域名黑名单预检服务接口
/// </summary>
public interface IDomainBlocklistChecker
{
    /// <summary>
    /// 检查域名是否被Anthropic黑名单拦截
    /// </summary>
    Task<DomainCheckResult> CheckAsync(string domain, CancellationToken cancellationToken = default);
}
