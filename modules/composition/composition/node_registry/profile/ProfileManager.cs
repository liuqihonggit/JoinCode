namespace JoinCode.Composition.NodeRegistry;

/// <summary>
/// ProfileManager 实现 — 运行期 Profile 动态切换
/// 通过 volatile 引用原子替换实现无锁切换，符合项目配置读写规范
/// </summary>
public sealed class ProfileManager : IProfileManager {
    private volatile ResolvedProfile _current;
    private readonly NodeProfileResolver _resolver;
    private readonly ILogger<ProfileManager>? _logger;

    /// <summary>当前活跃的 ResolvedProfile</summary>
    public ResolvedProfile Current => _current;

    /// <summary>Profile 切换前事件</summary>
    public event Func<ResolvedProfile, ResolvedProfile, ValueTask>? ProfileSwitching;

    /// <summary>
    /// 构造 ProfileManager
    /// </summary>
    /// <param name="resolver">Profile 解析器</param>
    /// <param name="initial">初始 ResolvedProfile</param>
    /// <param name="logger">日志记录器（可选）</param>
    public ProfileManager(NodeProfileResolver resolver, ResolvedProfile initial, ILogger<ProfileManager>? logger = null) {
        _resolver = resolver;
        _current = initial;
        _logger = logger;
    }

    /// <summary>切换到指定 Profile</summary>
    public async Task SwitchAsync(string profileName, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrEmpty(profileName);
        var newProfile = _resolver.Resolve(profileName);
        _logger?.LogInformation("Switching profile from '{OldName}' to '{NewName}'", _current.Name, profileName);

        if (ProfileSwitching is not null) {
            foreach (var handler in ProfileSwitching.GetInvocationList()) {
                try {
                    await ((Func<ResolvedProfile, ResolvedProfile, ValueTask>)handler)(_current, newProfile).ConfigureAwait(false);
                } catch (Exception ex) {
                    _logger?.LogWarning(ex, "ProfileSwitching handler threw exception during switch to '{NewName}'", profileName);
                }
            }
        }

        _current = newProfile;
        _logger?.LogInformation("Profile switched to '{ProfileName}'", profileName);
    }
}
