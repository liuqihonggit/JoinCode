namespace JoinCode.Abstractions.Configuration;

/// <summary>
/// 沙箱配置 — 对齐基准 <c>SandboxSettingsSchema</c>（claude-code-rev-main/src/entrypoints/sandboxTypes.ts:91-144）。
/// <para>默认值与基准逐项一致：<c>Enabled</c> 缺省 false、<c>AutoAllowBashIfSandboxed</c> 与
/// <c>AllowUnsandboxedCommands</c> 缺省 true（sandbox-adapter.ts:459-485）。</para>
/// <para>Phase 1 只消费 <c>Enabled/FailIfUnavailable/AutoAllowBashIfSandboxed/AllowUnsandboxedCommands/
/// ExcludedCommands/EnabledPlatforms</c>；network/filesystem 的细粒度清单一期先落契约（不可静默丢弃），
/// 由 Phase 2 接到 sandbox provider。</para>
/// </summary>
public sealed record SandboxConfig {
    /// <summary>是否启用沙箱 — 唯一总开关（基准 sandbox.enabled，缺省 false）。</summary>
    public bool Enabled { get; init; }

    /// <summary>启用但不可运行时是否拒绝启动（基准 sandbox.failIfUnavailable，缺省 false）。</summary>
    public bool FailIfUnavailable { get; init; }

    /// <summary>沙箱内的 Bash/PowerShell 是否免确认（基准 sandbox.autoAllowBashIfSandboxed，缺省 true）。</summary>
    public bool AutoAllowBashIfSandboxed { get; init; } = true;

    /// <summary>是否允许通过 dangerouslyDisableSandbox 跳出沙箱（基准 sandbox.allowUnsandboxedCommands，缺省 true）。</summary>
    public bool AllowUnsandboxedCommands { get; init; } = true;

    /// <summary>不进入沙箱的命令模式清单（基准 sandbox.excludedCommands；prefix/exact/wildcard 三档）。</summary>
    public List<string> ExcludedCommands { get; init; } = [];

    /// <summary>允许沙箱生效的平台（基准未公开的 enabledPlatforms；空 = 不限制）。</summary>
    public List<string> EnabledPlatforms { get; init; } = [];

    /// <summary>网络访问面（基准 sandbox.network）。</summary>
    public SandboxNetworkConfig Network { get; init; } = new();

    /// <summary>文件系统访问面（基准 sandbox.filesystem）。</summary>
    public SandboxFilesystemConfig Filesystem { get; init; } = new();

    /// <summary>违规忽略表（基准 sandbox.ignoreViolations，键为路径，值为规则名列表）。</summary>
    public Dictionary<string, List<string>> IgnoreViolations { get; init; } = [];

    /// <summary>允许更弱的嵌套沙箱（基准 sandbox.enableWeakerNestedSandbox）。</summary>
    public bool EnableWeakerNestedSandbox { get; init; }

    /// <summary>允许更弱的网络隔离（基准 sandbox.enableWeakerNetworkIsolation，仅 macOS 语义）。</summary>
    public bool EnableWeakerNetworkIsolation { get; init; }

    /// <summary>自定义 ripgrep 配置（基准 sandbox.ripgrep；bwrap 包装暂不消费，仅保持契约不丢失）。</summary>
    public SandboxRipgrepConfig? Ripgrep { get; init; }
}

/// <summary>沙箱内 ripgrep 配置 — 对齐基准 <c>sandbox.ripgrep</c>（command + 可选 args）。</summary>
public sealed record SandboxRipgrepConfig {
    /// <summary>ripgrep 可执行文件路径/命令。</summary>
    public required string Command { get; init; }

    /// <summary>附加参数。</summary>
    public List<string> Args { get; init; } = [];
}

/// <summary>沙箱网络配置 — 对齐基准 <c>SandboxNetworkConfigSchema</c>（sandboxTypes.ts:14-42）。</summary>
public sealed record SandboxNetworkConfig {
    /// <summary>允许访问的域名列表。</summary>
    public List<string> AllowedDomains { get; init; } = [];

    /// <summary>仅尊重受管设置里的域名（企业策略）。</summary>
    public bool AllowManagedDomainsOnly { get; init; }

    /// <summary>允许的 Unix socket 路径（仅 macOS 语义）。</summary>
    public List<string> AllowUnixSockets { get; init; } = [];

    /// <summary>允许全部 Unix socket（会关闭两端阻断）。</summary>
    public bool AllowAllUnixSockets { get; init; }

    /// <summary>允许本地端口绑定。</summary>
    public bool AllowLocalBinding { get; init; }

    /// <summary>HTTP 代理端口。</summary>
    public int? HttpProxyPort { get; init; }

    /// <summary>SOCKS 代理端口。</summary>
    public int? SocksProxyPort { get; init; }
}

/// <summary>沙箱文件系统配置 — 对齐基准 <c>SandboxFilesystemConfigSchema</c>（sandboxTypes.ts:47-86）。</summary>
public sealed record SandboxFilesystemConfig {
    /// <summary>额外允许写入的路径（与 Edit 允许规则合并）。</summary>
    public List<string> AllowWrite { get; init; } = [];

    /// <summary>额外禁止写入的路径（与 Edit 拒绝规则合并）。</summary>
    public List<string> DenyWrite { get; init; } = [];

    /// <summary>额外禁止读取的路径（与 Read 拒绝规则合并）。</summary>
    public List<string> DenyRead { get; init; } = [];

    /// <summary>在 denyRead 区域内重新允许读取的路径（优先于 DenyRead）。</summary>
    public List<string> AllowRead { get; init; } = [];

    /// <summary>仅使用受管设置里的允许读取路径。</summary>
    public bool AllowManagedReadPathsOnly { get; init; }
}
