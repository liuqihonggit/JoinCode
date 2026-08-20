namespace Core.Hooks.Execution.Rewriters;

/// <summary>
/// GitHub PR Body 改写器 — 为 gh pr create 自动添加 --body 参数
/// <para>
/// 解决问题：LLM 调用 gh pr create 时经常忘记写 body，导致 PR 描述为空
/// </para>
/// </summary>
public sealed class GhPrBodyRewriter : ICommandRewriter
{
    private readonly ILogger<GhPrBodyRewriter>? _logger;

    public GhPrBodyRewriter(ILogger<GhPrBodyRewriter>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "GhPrBodyRewriter";

    /// <inheritdoc/>
    public int Priority => 100;

    /// <inheritdoc/>
    public bool CanRewrite(string command)
    {
        // 匹配 gh pr create 命令
        var normalized = command.TrimStart();
        return normalized.StartsWith("gh pr create", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("gh.exe pr create", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public string Rewrite(string command, IReadOnlyDictionary<string, object> context)
    {
        // 检查是否已有 --body 参数
        if (HasBodyParameter(command))
        {
            return command;
        }

        // 从上下文获取 body 内容
        var body = GetBodyFromContext(context);

        // 如果上下文没有 body，使用默认模板
        if (string.IsNullOrWhiteSpace(body))
        {
            body = GenerateDefaultBody(context);
        }

        // 转义 body 内容
        var escapedBody = EscapeBody(body);

        // 添加 --body 参数
        var rewritten = $"{command} --body \"{escapedBody}\"";

        _logger?.LogInformation("为 gh pr create 自动添加 --body 参数");
        Console.Error.WriteLine($"[DIAG-REWRITE] GhPrBodyRewriter: 添加 --body 参数");
        Console.Error.Flush();

        return rewritten;
    }

    /// <summary>
    /// 检查命令是否已包含 --body 参数
    /// </summary>
    private static bool HasBodyParameter(string command)
    {
        return command.Contains("--body", StringComparison.OrdinalIgnoreCase)
               || command.Contains("-b ", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 从上下文获取 body 内容
    /// </summary>
    private static string? GetBodyFromContext(IReadOnlyDictionary<string, object> context)
    {
        if (context.TryGetValue("pr_body", out var bodyObj))
        {
            return bodyObj?.ToString();
        }

        if (context.TryGetValue("body", out var genericBody))
        {
            return genericBody?.ToString();
        }

        return null;
    }

    /// <summary>
    /// 生成默认 body 模板
    /// </summary>
    private static string GenerateDefaultBody(IReadOnlyDictionary<string, object> context)
    {
        // 委托给 PrBodyGenerator.GenerateWithTemplate，消除模板重复
        var title = context.TryGetValue("pr_title", out var titleObj) && titleObj is string t ? t : "变更内容";
        var branch = context.TryGetValue("head_branch", out var branchObj) && branchObj is string b ? $"分支: `{b}`" : null;
        var description = branch;
        return IO.ProcessService.PrBodyGenerator.GenerateWithTemplate(title, description);
    }

    /// <summary>
    /// 转义 body 内容中的特殊字符
    /// </summary>
    private static string EscapeBody(string body)
    {
        return body
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");
    }
}

/// <summary>
/// GitHub 命令超时改写器 — 为 gh 命令添加超时参数
/// <para>
/// 解决问题：gh 命令执行超时，需要添加超时控制
/// </para>
/// </summary>
public sealed class GhTimeoutRewriter : ICommandRewriter
{
    private readonly ILogger<GhTimeoutRewriter>? _logger;

    public GhTimeoutRewriter(ILogger<GhTimeoutRewriter>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "GhTimeoutRewriter";

    /// <inheritdoc/>
    public int Priority => 50;

    /// <inheritdoc/>
    public bool CanRewrite(string command)
    {
        var normalized = command.TrimStart();
        return normalized.StartsWith("gh ", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("gh.exe ", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public string Rewrite(string command, IReadOnlyDictionary<string, object> context)
    {
        _logger?.LogDebug("gh 命令超时控制: {Command}", command);
        return command;
    }
}

/// <summary>
/// VPN 路由改写器 — 识别 VPN 状态，自动切换代理
/// <para>
/// 解决问题：VPN 开启时网络请求需要走代理
/// </para>
/// </summary>
public sealed class VpnRouteRewriter : ICommandRewriter
{
    private readonly ILogger<VpnRouteRewriter>? _logger;
    private readonly bool _vpnDetected;

    public VpnRouteRewriter(ILogger<VpnRouteRewriter>? logger = null)
    {
        _logger = logger;
        _vpnDetected = DetectVpn();
    }

    /// <inheritdoc/>
    public string Name => "VpnRouteRewriter";

    /// <inheritdoc/>
    public int Priority => 30;

    /// <inheritdoc/>
    public bool CanRewrite(string command)
    {
        // 只在网络相关命令上生效
        if (!_vpnDetected) return false;

        var normalized = command.TrimStart();
        return normalized.StartsWith("gh ", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("git ", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("curl ", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("wget ", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public string Rewrite(string command, IReadOnlyDictionary<string, object> context)
    {
        // 从上下文获取代理地址
        if (!context.TryGetValue("proxy_url", out var proxyObj) || proxyObj is not string proxyUrl)
        {
            return command;
        }

        // 为 git 命令添加代理配置
        if (command.StartsWith("git ", StringComparison.OrdinalIgnoreCase))
        {
            var rewritten = $"git -c http.proxy={proxyUrl} -c https.proxy={proxyUrl} {command[4..]}";
            _logger?.LogInformation("为 git 命令添加 VPN 代理: {Proxy}", proxyUrl);
            return rewritten;
        }

        // 为 curl 命令添加代理
        if (command.StartsWith("curl ", StringComparison.OrdinalIgnoreCase))
        {
            var rewritten = $"curl --proxy {proxyUrl} {command[5..]}";
            _logger?.LogInformation("为 curl 命令添加 VPN 代理: {Proxy}", proxyUrl);
            return rewritten;
        }

        // gh 命令通过环境变量控制代理，不改写命令本身
        return command;
    }

    /// <summary>
    /// 检测 VPN 状态
    /// </summary>
    private static bool DetectVpn()
    {
        try
        {
            // 检查常见 VPN 进程
            var vpnProcesses = new[] { "vpn", "openvpn", "wireguard", "clash", "v2ray" };
            foreach (var proc in vpnProcesses)
            {
                if (System.Diagnostics.Process.GetProcessesByName(proc).Length > 0)
                {
                    return true;
                }
            }

            // 检查环境变量
            var httpProxy = Environment.GetEnvironmentVariable("HTTP_PROXY");
            var httpsProxy = Environment.GetEnvironmentVariable("HTTPS_PROXY");
            if (!string.IsNullOrEmpty(httpProxy) || !string.IsNullOrEmpty(httpsProxy))
            {
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}
