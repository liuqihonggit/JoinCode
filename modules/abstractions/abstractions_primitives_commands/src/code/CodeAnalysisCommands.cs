namespace JoinCode.Abstractions.Commands;

public sealed class AnalyzeCSharpCodeCommand {
    /// <summary>获取要分析的代码。</summary>
    public string Code { get; }
    /// <summary>获取分析焦点。</summary>
    public string Focus { get; }

    /// <summary>构造 C# 代码分析命令。</summary>
    /// <param name="code">要分析的代码。</param>
    /// <param name="focus">分析焦点。</param>
    public AnalyzeCSharpCodeCommand(string code, string focus = "all") {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        if (code.Length < 10) throw new ArgumentException("[ABS001] 代码至少需要 10 个字符", nameof(code));
        Focus = focus?.ToLowerInvariant() ?? "all";
    }
}

public sealed class FindBugsCommand {
    /// <summary>获取要分析的代码。</summary>
    public string Code { get; }
    /// <summary>获取严重级别。</summary>
    public string Severity { get; }

    /// <summary>构造查找 Bug 命令。</summary>
    /// <param name="code">要分析的代码。</param>
    /// <param name="severity">严重级别。</param>
    public FindBugsCommand(string code, string severity = "all") {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        if (code.Length < 10) throw new ArgumentException("[ABS002] 代码至少需要 10 个字符", nameof(code));
        Severity = severity?.ToLowerInvariant() ?? "all";
    }
}

public sealed class OptimizeCodeCommand {
    /// <summary>获取要优化的代码。</summary>
    public string Code { get; }
    /// <summary>获取优化目标。</summary>
    public string Target { get; }

    /// <summary>构造代码优化命令。</summary>
    /// <param name="code">要优化的代码。</param>
    /// <param name="target">优化目标。</param>
    public OptimizeCodeCommand(string code, string target = "all") {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        if (code.Length < 10) throw new ArgumentException("[ABS003] 代码至少需要 10 个字符", nameof(code));
        Target = target?.ToLowerInvariant() ?? "all";
    }
}

public sealed class SecurityAuditCommand {
    /// <summary>获取要审计的代码。</summary>
    public string Code { get; }
    /// <summary>获取审计类型。</summary>
    public string AuditType { get; }

    /// <summary>构造安全审计命令。</summary>
    /// <param name="code">要审计的代码。</param>
    /// <param name="auditType">审计类型。</param>
    public SecurityAuditCommand(string code, string auditType = AnalysisTypeEnumConstants.General) {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        if (code.Length < 10) throw new ArgumentException("[ABS004] 代码至少需要 10 个字符", nameof(code));
        AuditType = auditType?.ToLowerInvariant() ?? AnalysisTypeEnumConstants.General;
    }
}