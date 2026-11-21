namespace JoinCode.Abstractions.Security.Sandbox;

/// <summary>
/// 逐命令沙箱包装请求 — 把 settings 的 filesystem/network 清单翻译成 provider 可消费的形态。
/// <para>语义对齐基准 <c>utils/sandbox/sandbox-adapter.ts:359-380</c> 的 SandboxRuntimeConfig。</para>
/// </summary>
public sealed partial class SandboxWrapRequest {
    /// <summary>待执行命令（原始 shell 命令串）。</summary>
    public required string Command { get; init; }

    /// <summary>命令工作目录（宿主绝对路径）。</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>额外允许写入的路径（宿主绝对路径）。</summary>
    public List<string> AllowWritePaths { get; init; } = [];

    /// <summary>额外禁止写入的路径。</summary>
    public List<string> DenyWritePaths { get; init; } = [];

    /// <summary>额外允许读取的路径（优先于 DenyReadPaths）。</summary>
    public List<string> AllowReadPaths { get; init; } = [];

    /// <summary>额外禁止读取的路径。</summary>
    public List<string> DenyReadPaths { get; init; } = [];

    /// <summary>是否限制网络（false = 允许全部网络）。</summary>
    public bool RestrictNetwork { get; init; } = true;

    /// <summary>允许访问的域名清单 — bwrap 原生不支持域名过滤，由 provider 决定降级策略。</summary>
    public List<string> AllowedDomains { get; init; } = [];

    /// <summary>允许本地端口绑定。</summary>
    public bool AllowLocalBinding { get; init; }

    /// <summary>允许访问的 Unix socket 路径（bwrap 不支持按 socket 过滤，由 provider 显式告警）。</summary>
    public List<string> AllowUnixSockets { get; init; } = [];

    /// <summary>允许全部 Unix socket（bwrap 不支持，由 provider 显式告警）。</summary>
    public bool AllowAllUnixSockets { get; init; }

    /// <summary>HTTP 代理端口（bwrap 无内置 MITM 代理，由 provider 显式告警）。</summary>
    public int? HttpProxyPort { get; init; }

    /// <summary>SOCKS 代理端口（同上）。</summary>
    public int? SocksProxyPort { get; init; }

    /// <summary>允许更弱的嵌套沙箱。</summary>
    public bool EnableWeakerNestedSandbox { get; init; }

    /// <summary>允许更弱的网络隔离（基准仅 macOS trustd 语义）。</summary>
    public bool EnableWeakerNetworkIsolation { get; init; }

    /// <summary>违规忽略表（bwrap 无违规上报通道，由 provider 显式告警）。</summary>
    public Dictionary<string, List<string>> IgnoreViolations { get; init; } = [];

    /// <summary>自定义 ripgrep 命令（null = 未配置；bwrap 包装暂不消费，由 provider 显式告警）。</summary>
    public string? RipgrepCommand { get; init; }
}
