namespace Core.Agents.Doctor;


/// <summary>
/// 默认自举安全守卫 — 6 条审核规则防止 Agent 破坏自身
/// </summary>
public sealed class DefaultBootstrapGuard : IBootstrapGuard
{
    private readonly IFileSystem _fs;
    private readonly Dictionary<string, DateTimeOffset> _lastModificationByFile = new();
    private readonly TimeSpan _rateLimitInterval = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 构造默认自举安全守卫
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    public DefaultBootstrapGuard(IFileSystem fs)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
    }

    /// <summary>
    /// 审核修改请求 — 依次执行 6 条安全规则：守卫文件保护、变更行数告警、Register 特性保护、频率限制、配置文件保护、基本语法检查
    /// </summary>
    /// <param name="request">修改请求</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>审核决策</returns>
    public Task<GuardDecision> ReviewAsync(
        BootstrapModificationRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var warnings = new List<string>();

        if (IsGuardOrVaultFile(request.TargetPath))
        {
            return Task.FromResult(new GuardDecision
            {
                Approved = false,
                Reason = "禁止修改安全守卫相关代码"
            });
        }

        var changedLines = CountChangedLines(request.OriginalContent, request.ProposedContent);
        if (changedLines > 50)
        {
            warnings.Add($"变更 {changedLines} 行，建议人工审核");
        }

        if (RemovedRegisterAttribute(request.OriginalContent, request.ProposedContent))
        {
            return Task.FromResult(new GuardDecision
            {
                Approved = false,
                Reason = "禁止删除 [Register] 特性，会破坏 DI 注册"
            });
        }

        if (IsRateLimited(request.TargetPath))
        {
            return Task.FromResult(new GuardDecision
            {
                Approved = false,
                Reason = "修改频率超限，同一文件 10 分钟内只能修改 1 次"
            });
        }

        if (IsProjectConfigFile(request.TargetPath))
        {
            return Task.FromResult(new GuardDecision
            {
                Approved = false,
                Reason = "禁止修改项目配置文件，可能破坏编译"
            });
        }

        if (!BasicSyntaxCheck(request.ProposedContent))
        {
            return Task.FromResult(new GuardDecision
            {
                Approved = false,
                Reason = "修改后代码基本语法检查失败"
            });
        }

        _lastModificationByFile[request.TargetPath] = DateTimeOffset.UtcNow;

        return Task.FromResult(new GuardDecision
        {
            Approved = true,
            Warnings = warnings
        });
    }

    /// <summary>
    /// 判断路径是否为安全守卫或保险库相关文件
    /// </summary>
    /// <param name="path">文件路径</param>
    /// <returns>是守卫/保险库文件返回 true</returns>
    internal static bool IsGuardOrVaultFile(string path)
    {
        return path.Contains("Guard", StringComparison.OrdinalIgnoreCase)
            || path.Contains("Vault", StringComparison.OrdinalIgnoreCase)
            || path.Contains("BootstrapGuard", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 统计原始内容与修改后内容之间的变更行数
    /// </summary>
    /// <param name="original">原始内容</param>
    /// <param name="proposed">修改后内容</param>
    /// <returns>变更行数</returns>
    internal static int CountChangedLines(string original, string proposed)
    {
        var originalLines = original.Split('\n');
        var proposedLines = proposed.Split('\n');

        var changed = Math.Abs(originalLines.Length - proposedLines.Length);
        var minLen = Math.Min(originalLines.Length, proposedLines.Length);

        for (var i = 0; i < minLen; i++)
        {
            if (originalLines[i] != proposedLines[i])
                changed++;
        }

        return changed;
    }

    /// <summary>
    /// 检测是否删除了 [Register] 特性 — 删除会破坏 DI 注册
    /// </summary>
    /// <param name="original">原始内容</param>
    /// <param name="proposed">修改后内容</param>
    /// <returns>删除了 Register 特性返回 true</returns>
    internal static bool RemovedRegisterAttribute(string original, string proposed)
    {
        var originalRegisters = CountOccurrences(original, "[Register");
        var proposedRegisters = CountOccurrences(proposed, "[Register");
        return proposedRegisters < originalRegisters;
    }

    /// <summary>
    /// 判断路径是否为项目配置文件（.csproj/.props/.targets/Directory.Build）
    /// </summary>
    /// <param name="path">文件路径</param>
    /// <returns>是配置文件返回 true</returns>
    internal static bool IsProjectConfigFile(string path)
    {
        return path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".props", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase)
            || path.Contains("Directory.Build", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 基本语法检查 — 校验大括号配对等简单规则
    /// </summary>
    /// <param name="content">待检查代码内容</param>
    /// <returns>通过基本检查返回 true</returns>
    internal static bool BasicSyntaxCheck(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;

        var openBraces = CountOccurrences(content, "{");
        var closeBraces = CountOccurrences(content, "}");
        if (Math.Abs(openBraces - closeBraces) > 0) return false;

        return true;
    }

    private bool IsRateLimited(string targetPath)
    {
        if (!_lastModificationByFile.TryGetValue(targetPath, out var lastTime))
            return false;

        return DateTimeOffset.UtcNow - lastTime < _rateLimitInterval;
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var idx = 0;
        while ((idx = source.IndexOf(value, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            idx += value.Length;
        }
        return count;
    }
}
