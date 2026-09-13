
namespace McpClient;

/// <summary>
/// 静态认证提供者基类 — 为不涉及令牌刷新与 Step-Up 流程的静态认证方式（ApiKey/Bearer/Basic）提供公共实现。
/// 派生类只需重写 AuthType、IsAuthenticated、GetAuthHeadersAsync、GetAccessTokenAsync。
/// </summary>
public abstract class StaticAuthProviderBase : IMcpAuthProvider
{
    /// <summary>认证类型 — 由派生类指定。</summary>
    public abstract McpAuthType AuthType { get; }

    /// <summary>认证是否有效 — 由派生类根据自身凭证状态判断。</summary>
    public abstract bool IsAuthenticated { get; }

    /// <summary>当前 Step-Up 待处理的 scope — 静态提供者不支持 Step-Up,始终返回 null。</summary>
    public string? StepUpPendingScope => null;

    /// <summary>是否需要 Step-Up 认证 — 静态提供者始终返回 false。</summary>
    public bool NeedsStepUp => false;

    /// <summary>获取认证头 — 由派生类实现具体拼装逻辑。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>认证头字典。</returns>
    public abstract Task<Dictionary<string, string>> GetAuthHeadersAsync(CancellationToken cancellationToken = default);

    /// <summary>获取访问令牌 — 由派生类实现具体取值逻辑。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>访问令牌字符串,无令牌时返回 null。</returns>
    public abstract Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>刷新认证令牌 — 静态提供者无需刷新,始终返回 true。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>始终返回 true。</returns>
    public Task<bool> RefreshAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    /// <summary>标记 Step-Up 认证待处理 — 静态提供者不支持,空操作。</summary>
    /// <param name="scope">待提升的 scope 字符串。</param>
    public void MarkStepUpPending(string scope) { }

    /// <summary>清除 Step-Up 状态 — 静态提供者不支持,空操作。</summary>
    public void ClearStepUpPending() { }
}
