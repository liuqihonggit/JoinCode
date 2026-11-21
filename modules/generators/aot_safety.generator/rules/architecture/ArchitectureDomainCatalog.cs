namespace AotSafety.Generator.Rules;

/// <summary>
/// 18 个契约域的单一清单 — 分析器侧 JCC12001 与门禁侧的域名校验共用同一份语义。
/// 注意: 门禁是 PowerShell，无法直接引用本类；修改清单时必须同步
/// non_deliverables_tools/architecture/ArchitectureGate.ps1 的 $script:ContractDomains。
/// </summary>
public static class ArchitectureDomainCatalog {
    private static readonly HashSet<string> Known = new(StringComparer.Ordinal) {
        "abstractions",
        "apps",
        "async_primitives",
        "collections",
        "cognition",
        "composition",
        "generators",
        "guard",
        "llm",
        "mcp",
        "perception",
        "persistence",
        "plugins",
        "services",
        "storage",
        "tools",
        "native_tui",
        "native_gui",
    };

    public static bool IsKnown(string? domain) {
        var value = domain?.Trim();
        if (value is null || value.Length == 0) return false;
        return Known.Contains(value);
    }

    public static IReadOnlyList<string> SplitDomains(string? raw) {
        var value = raw;
        if (value is null || value.Trim().Length == 0) return Array.Empty<string>();
        return value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static item => item.Trim())
            .Where(static item => item.Length > 0)
            .ToArray();
    }
}
