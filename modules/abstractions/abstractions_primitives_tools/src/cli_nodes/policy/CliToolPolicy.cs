namespace JoinCode.Abstractions.Tools;

/// <summary>CLI 工具或参数权限效果。</summary>
public enum CliPolicyEffect {
    /// <summary>允许执行；仍需通过 agent 工具域与动态风险检查。</summary>
    Allow,
    /// <summary>需要用户或后台队列确认。</summary>
    Ask,
    /// <summary>硬拒绝，不可被权限模式或 AI 判定推翻。</summary>
    Deny
}

/// <summary>
/// CLI 工具参数申请的访问能力 — 权限 YAML 的有限字符串集合。
/// <para>能力描述“需要什么访问”，具体路径、命令与网络判定继续复用现有权限管线。</para>
/// </summary>
public enum CliPolicyCapability {
    /// <summary>读取工作区或显式允许的路径。</summary>
    WorkspaceRead,
    /// <summary>写入工作区或显式允许的路径。</summary>
    WorkspaceWrite,
    /// <summary>删除文件系统对象。</summary>
    FileSystemDelete,
    /// <summary>读取外部网络资源。</summary>
    NetworkRead,
    /// <summary>向外部网络写入或发送数据。</summary>
    NetworkWrite,
    /// <summary>启动进程或执行命令。</summary>
    ProcessExecute,
    /// <summary>修改会话、任务或代理状态。</summary>
    SessionMutate,
    /// <summary>修改权限、配置或安全策略。</summary>
    PermissionMutate
}

/// <summary>CLI 能力枚举与权限 YAML 字符串的转换。</summary>
public static class CliPolicyCapabilityExtensions {
    /// <summary>转换为权限 YAML 使用的字符串。</summary>
    /// <param name="capability">能力枚举值。</param>
    /// <returns>稳定的 snake_case 字符串。</returns>
    public static string ToValue(this CliPolicyCapability capability) => capability switch {
        CliPolicyCapability.WorkspaceRead => "workspace_read",
        CliPolicyCapability.WorkspaceWrite => "workspace_write",
        CliPolicyCapability.FileSystemDelete => "filesystem_delete",
        CliPolicyCapability.NetworkRead => "network_read",
        CliPolicyCapability.NetworkWrite => "network_write",
        CliPolicyCapability.ProcessExecute => "process_execute",
        CliPolicyCapability.SessionMutate => "session_mutate",
        CliPolicyCapability.PermissionMutate => "permission_mutate",
        _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, "未知 CLI 权限能力")
    };

    /// <summary>尝试把权限 YAML 字符串解析为能力枚举。</summary>
    /// <param name="value">YAML 中的能力字符串。</param>
    /// <param name="capability">解析成功时的能力值。</param>
    /// <returns>命中时返回 true。</returns>
    public static bool TryParse(string value, out CliPolicyCapability capability) {
        foreach (var candidate in All) {
            if (string.Equals(candidate.ToValue(), value, StringComparison.Ordinal)) {
                capability = candidate;
                return true;
            }
        }
        capability = default;
        return false;
    }

    /// <summary>全部能力值 — 错误提示与静态校验共用同一份清单。</summary>
    public static IReadOnlyList<CliPolicyCapability> All { get; } = [
        CliPolicyCapability.WorkspaceRead,
        CliPolicyCapability.WorkspaceWrite,
        CliPolicyCapability.FileSystemDelete,
        CliPolicyCapability.NetworkRead,
        CliPolicyCapability.NetworkWrite,
        CliPolicyCapability.ProcessExecute,
        CliPolicyCapability.SessionMutate,
        CliPolicyCapability.PermissionMutate
    ];
}

/// <summary>单个 CLI 参数的权限条目。</summary>
public sealed record CliToolParameterPolicy {
    /// <summary>参数 canonical 名（不带 <c>--</c>）。</summary>
    public required string Name { get; init; }

    /// <summary>参数权限效果。</summary>
    public required CliPolicyEffect Effect { get; init; }

    /// <summary>参数申请的访问能力；无额外能力时为空。</summary>
    public IReadOnlyList<CliPolicyCapability> Capabilities { get; init; } = [];
}

/// <summary>单个 CLI 工具的权限条目。</summary>
public sealed record CliToolPolicy {
    /// <summary>工具 canonical ID。</summary>
    public required string ToolId { get; init; }

    /// <summary>工具级权限效果。</summary>
    public required CliPolicyEffect Effect { get; init; }

    /// <summary>全部 canonical 参数的权限条目；无参数工具必须显式声明空集合。</summary>
    public required IReadOnlyList<CliToolParameterPolicy> Parameters { get; init; }
}

/// <summary>一个可执行文件的完整 CLI 权限策略。</summary>
public sealed record CliToolPolicyManifest {
    /// <summary>策略 schema 版本。</summary>
    public required string SchemaVersion { get; init; }

    /// <summary>策略标识，如 <c>cli.file</c>。</summary>
    public required string PolicyId { get; init; }

    /// <summary>策略内容版本，用于审计与 grant 绑定。</summary>
    public required int PolicyVersion { get; init; }

    /// <summary>允许承载该策略的可执行文件名。</summary>
    public required IReadOnlyList<string> Executables { get; init; }

    /// <summary>工具权限条目。</summary>
    public required IReadOnlyList<CliToolPolicy> Tools { get; init; }
}
