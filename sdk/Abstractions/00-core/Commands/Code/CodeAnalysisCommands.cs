namespace JoinCode.Abstractions.Commands;

public sealed class AnalyzeCSharpCodeCommand
{
    public string Code { get; }
    public string Focus { get; }

    public AnalyzeCSharpCodeCommand(string code, string focus = "all")
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        if (code.Length < 10) throw new ArgumentException("代码至少需要 10 个字符", nameof(code));
        Focus = focus?.ToLowerInvariant() ?? "all";
    }
}

public sealed class FindBugsCommand
{
    public string Code { get; }
    public string Severity { get; }

    public FindBugsCommand(string code, string severity = "all")
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        if (code.Length < 10) throw new ArgumentException("代码至少需要 10 个字符", nameof(code));
        Severity = severity?.ToLowerInvariant() ?? "all";
    }
}

public sealed class OptimizeCodeCommand
{
    public string Code { get; }
    public string Target { get; }

    public OptimizeCodeCommand(string code, string target = "all")
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        if (code.Length < 10) throw new ArgumentException("代码至少需要 10 个字符", nameof(code));
        Target = target?.ToLowerInvariant() ?? "all";
    }
}

public sealed class SecurityAuditCommand
{
    public string Code { get; }
    public string AuditType { get; }

    public SecurityAuditCommand(string code, string auditType = "general")
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        if (code.Length < 10) throw new ArgumentException("代码至少需要 10 个字符", nameof(code));
        AuditType = auditType?.ToLowerInvariant() ?? "general";
    }
}

