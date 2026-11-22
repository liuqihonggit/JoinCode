namespace Core.Security.Sandbox.Providers;

/// <summary>
/// Bubblewrap 逐命令包装 — 把 <see cref="SandboxWrapRequest"/> 翻译成 <c>bwrap</c> 参数串。
/// <para>与 <see cref="BubblewrapSandboxProvider.ExecuteInSandboxAsync"/> 的会话沙箱不同，
/// 这里把每条命令直接包进一次性 bwrap 实例，对齐基准 <c>SandboxManager.wrapWithSandbox</c>。</para>
/// <para>安全取舍：bwrap 原生不支持域名级过滤，<c>AllowedDomains</c> 非空时 fail-closed（仍断网）并告警。</para>
/// </summary>
internal static class BwrapCommandBuilder {
    private const string BwrapExecutable = "bwrap";

    /// <summary>基础只读系统路径 — 沙箱内命令需要的最小运行时。</summary>
    private static readonly string[] BaseSystemPaths = ["/usr", "/bin", "/lib", "/lib64", "/sbin", "/etc"];

    /// <summary>
    /// 构建包装命令。
    /// </summary>
    /// <param name="request">包装请求。</param>
    /// <param name="pathExists">路径存在性探测（默认视为存在，便于纯参数测试）。</param>
    /// <param name="pathIsDirectory">路径是否目录（用于 deny 规则选择 tmpfs 或 /dev/null）。</param>
    /// <returns>包装结果；永不返回 null（provider 决定是否调用）。</returns>
    public static SandboxWrappedCommand Build(
        SandboxWrapRequest request,
        Func<string, bool>? pathExists = null,
        Func<string, bool>? pathIsDirectory = null) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(request.Command);
        ArgumentException.ThrowIfNullOrEmpty(request.WorkingDirectory);

        var exists = pathExists ?? (_ => true);
        var isDirectory = pathIsDirectory ?? (_ => true);
        var warnings = new List<string>();

        var args = new List<string> { "--unshare-all", "--die-with-parent" };

        if (!request.RestrictNetwork) {
            args.Add("--share-net");
        } else if (request.AllowedDomains.Count > 0) {
            warnings.Add("bwrap 不支持域名级过滤：allowedDomains 已 fail-closed（网络仍被阻断）");
        }

        if (request.RestrictNetwork && request.AllowLocalBinding) {
            warnings.Add("bwrap 不支持 network.allowLocalBinding：已忽略");
        }

        if (request.AllowUnixSockets.Count > 0) {
            warnings.Add("bwrap 不支持 network.allowUnixSockets：已忽略");
        }

        if (request.AllowAllUnixSockets) {
            warnings.Add("bwrap 不支持 network.allowAllUnixSockets：已忽略");
        }

        if (request.HttpProxyPort is not null || request.SocksProxyPort is not null) {
            warnings.Add("bwrap 无内置代理端口能力：network.httpProxyPort/socksProxyPort 已忽略");
        }

        if (request.EnableWeakerNestedSandbox) {
            warnings.Add("bwrap 不支持 enableWeakerNestedSandbox：已忽略");
        }

        if (request.EnableWeakerNetworkIsolation) {
            warnings.Add("bwrap 不支持 enableWeakerNetworkIsolation（macOS trustd 语义）：已忽略");
        }

        if (request.IgnoreViolations.Count > 0) {
            warnings.Add("bwrap 无违规上报通道：ignoreViolations 已忽略");
        }

        if (request.RipgrepCommand is { Length: > 0 }) {
            warnings.Add("bwrap 包装不消费 sandbox.ripgrep：自定义 ripgrep 已忽略");
        }

        foreach (var path in BaseSystemPaths) {
            if (exists(path)) {
                AddBind(args, "--ro-bind", path, path);
            }
        }

        // 不挂载 /proc：容器化宿主的 seccomp 常拒绝 proc mount，会导致整个 bwrap 启动失败；
        // 沙箱内命令因此读不到 /proc（安全上更保守），需要 proc 的工具应在后续按能力探测再开放。
        args.AddRange(["--dev", "/dev", "--tmpfs", "/tmp"]);

        var workingDirectory = NormalizePath(request.WorkingDirectory);
        AddBind(args, "--bind", workingDirectory, workingDirectory);

        var allowReadOverrides = new List<string>();
        foreach (var path in request.AllowWritePaths) {
            if (TryPreparePath(path, request.WorkingDirectory, exists, warnings, "allowWrite", out var fullPath)) {
                AddBind(args, "--bind", fullPath, fullPath);
            }
        }

        foreach (var path in request.AllowReadPaths) {
            if (TryPreparePath(path, request.WorkingDirectory, exists, warnings, "allowRead", out var fullPath)) {
                AddBind(args, "--ro-bind", fullPath, fullPath);
                allowReadOverrides.Add(fullPath);
            }
        }

        foreach (var path in request.DenyWritePaths) {
            if (TryPreparePath(path, request.WorkingDirectory, exists, warnings, "denyWrite", out var fullPath)) {
                AddDenyMask(args, fullPath, isDirectory);
            }
        }

        foreach (var path in request.DenyReadPaths) {
            if (TryPreparePath(path, request.WorkingDirectory, exists, warnings, "denyRead", out var fullPath)) {
                AddDenyMask(args, fullPath, isDirectory);
            }
        }

        // allowRead 显式优先于 denyRead（基准 schema 语义），在 deny 之后重新覆盖。
        foreach (var path in allowReadOverrides) {
            AddBind(args, "--ro-bind", path, path);
        }

        args.AddRange(["--chdir", workingDirectory, "--", "/bin/sh", "-c"]);

        var commandLine = string.Join(' ', args.Select(ShellCommandEscape.EscapeForSingleQuotedShell));
        var wrapped = $"{ShellCommandEscape.EscapeForSingleQuotedShell(BwrapExecutable)} {commandLine} {ShellCommandEscape.EscapeForSingleQuotedShell(request.Command)}";

        return new SandboxWrappedCommand {
            Command = wrapped,
            ProviderType = SandboxType.Bubblewrap,
            Warning = warnings.Count > 0 ? string.Join("；", warnings) : null,
        };
    }

    private static void AddBind(List<string> args, string mode, string source, string destination) {
        AddArg(args, mode);
        AddArg(args, source);
        AddArg(args, destination);
    }

    private static void AddDenyMask(List<string> args, string path, Func<string, bool> isDirectory) {
        if (isDirectory(path)) {
            args.AddRange(["--tmpfs", path]);
            return;
        }

        AddBind(args, "--ro-bind", "/dev/null", path);
    }

    private static void AddArg(List<string> args, string value) => args.Add(value);

    private static bool TryPreparePath(
        string path,
        string workingDirectory,
        Func<string, bool> exists,
        List<string> warnings,
        string settingName,
        out string fullPath) {
        fullPath = NormalizePath(path, workingDirectory);
        if (!exists(fullPath)) {
            warnings.Add($"sandbox.filesystem.{settingName} 路径不存在，已跳过: {fullPath}");
            return false;
        }

        return true;
    }

    /// <summary>路径归一化 — Unix 绝对路径原样保留，相对路径按工作目录展开，<c>~</c> 展开到 HOME。</summary>
    internal static string NormalizePath(string path, string? workingDirectory = null) {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var candidate = path.Trim();
        if (candidate == "~" || candidate.StartsWith("~/", StringComparison.Ordinal)) {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            candidate = candidate == "~" ? home : Path.Combine(home, candidate[2..]);
        } else if (!candidate.StartsWith('/')) {
            candidate = Path.Combine(workingDirectory ?? Environment.CurrentDirectory, candidate);
        }

        // 去掉尾部 /（根路径除外），避免参数出现 `//` 或空段。
        while (candidate.Length > 1 && candidate.EndsWith('/')) {
            candidate = candidate[..^1];
        }

        return candidate;
    }
}
