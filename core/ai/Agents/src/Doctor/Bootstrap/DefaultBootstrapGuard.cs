namespace Core.Agents.Doctor;


/// <summary>
/// 默认自举安全守卫 — 6 条审核规则防止 Agent 破坏自身
/// </summary>
public sealed class DefaultBootstrapGuard : IBootstrapGuard
{
    private readonly IFileSystem _fs;
    private readonly Dictionary<string, DateTimeOffset> _lastModificationByFile = new();
    private readonly TimeSpan _rateLimitInterval = TimeSpan.FromMinutes(10);

    public DefaultBootstrapGuard(IFileSystem fs)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
    }

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

    internal static bool IsGuardOrVaultFile(string path)
    {
        return path.Contains("Guard", StringComparison.OrdinalIgnoreCase)
            || path.Contains("Vault", StringComparison.OrdinalIgnoreCase)
            || path.Contains("BootstrapGuard", StringComparison.OrdinalIgnoreCase);
    }

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

    internal static bool RemovedRegisterAttribute(string original, string proposed)
    {
        var originalRegisters = CountOccurrences(original, "[Register");
        var proposedRegisters = CountOccurrences(proposed, "[Register");
        return proposedRegisters < originalRegisters;
    }

    internal static bool IsProjectConfigFile(string path)
    {
        return path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".props", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase)
            || path.Contains("Directory.Build", StringComparison.OrdinalIgnoreCase);
    }

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
