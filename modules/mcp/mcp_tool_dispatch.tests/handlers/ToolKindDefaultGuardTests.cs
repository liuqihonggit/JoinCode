namespace McpToolRegistry.Tests;

/// <summary>
/// 工具 Kind 默认值守卫（TASK061 同批 — 巨型请求修复回归防护）—
/// 锁定三契约防 466KB 巨型请求复活：
/// ①特性默认值 Mcp（[McpToolDispatch] 未声明 Kind 的工具走两阶段加载组，首次只发名称）；
/// ②生成器产物默认 mcp（fallback "mcp" 而非 "system" — Sandbox 类无显式 Kind 的真实样本）；
/// ③核心工具白名单显式 System（File/Search/CodeExecution/Agent — 对齐基准 Claude Code 核心工具集
/// Read/Write/Edit/Grep/Glob/Bash/Task，这些发完整 schema）。
/// 回归形态：任一被改回 system → 506 工具全量 schema 466KB 巨型请求 → 10s 超时撞断 → 空回复。
/// </summary>
public class ToolKindDefaultGuardTests
{
    /// <summary>特性默认值 — 未显式声明 Kind 时为 Mcp（非 System）。</summary>
    [Fact]
    public void AttributeDefault_Kind_IsMcpNotSystem()
    {
        new McpToolDispatchAttribute("probe").Kind.Should().Be(ToolKind.Mcp);
    }

    /// <summary>生成器产物默认 — Sandbox 类（无显式 Kind 真实样本）生成的工具 Kind 为 Mcp
    ///（两阶段加载组：首次请求只发名称，tool_description_request 二阶段补齐）。</summary>
    [Fact]
    public void GeneratedDefault_NoExplicitKind_RegistersAsMcp()
    {
        var categories = GeneratedToolHandlerRegistration_JoinCode_McpToolDispatch.GetAvailableToolCategories();

        var sandboxTools = categories.Values
            .SelectMany(list => list)
            .Where(e => e.Name.Contains("sandbox", StringComparison.OrdinalIgnoreCase))
            .ToList();

        sandboxTools.Should().NotBeEmpty("SandboxToolHandlers 无显式 Kind — 生成器默认注册");
        sandboxTools.Should().OnlyContain(e => e.Kind == ToolKind.Mcp);
    }

    /// <summary>核心工具白名单 — FileToolHandlers 显式 System（完整 schema 直发，
    /// 对齐基准 Claude Code 核心工具集）。</summary>
    [Fact]
    public void CoreFileTools_ExplicitSystem_KindIsSystem()
    {
        var kind = typeof(Tools.Handlers.FileToolHandlers)
            .GetCustomAttributes(typeof(McpToolDispatchAttribute), false)
            .Cast<McpToolDispatchAttribute>()
            .FirstOrDefault()?.Kind;

        kind.Should().Be(ToolKind.System, "核心文件工具（Read/Write/Edit）属基准核心集，须发完整 schema");
    }
}
