namespace JoinCode.Abstractions.Interfaces;

public interface IAgentToolRestrictions {
    /// <summary>获取指定权限模式允许的工具集合。</summary>
    /// <param name="mode">权限模式。</param>
    IReadOnlySet<string> GetAllowedTools(PermissionMode mode);
    /// <summary>获取指定权限模式拒绝的工具集合。</summary>
    /// <param name="mode">权限模式。</param>
    IReadOnlySet<string> GetDeniedTools(PermissionMode mode);
    /// <summary>判断工具在指定权限模式下是否允许。</summary>
    /// <param name="toolName">工具名称。</param>
    /// <param name="mode">权限模式。</param>
    bool IsToolAllowedForMode(string toolName, PermissionMode mode);
}