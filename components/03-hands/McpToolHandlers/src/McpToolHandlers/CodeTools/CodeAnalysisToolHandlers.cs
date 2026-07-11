


namespace McpToolHandlers;

/// <summary>
/// 代码分析工具处理器 - 提供代码分析、Bug 查找、优化建议、安全审计等功能
/// </summary>
[McpToolHandler(ToolCategory.CodeAnalysis, Optional = true)]
public class CodeAnalysisToolHandlers
{
    private readonly IQueryEngine _queryEngine;

    public CodeAnalysisToolHandlers(IQueryEngine queryEngine)
    {
        _queryEngine = queryEngine ?? throw new ArgumentNullException(nameof(queryEngine));
    }

    /// <summary>
    /// 分析 C# 代码并提供建议
    /// </summary>
    [McpTool(CodeToolNameConstants.AnalyzeCsharpCode, "Analyze C# code quality and provide improvement suggestions", "code_analysis")]
    public async Task<ToolResult> AnalyzeCSharpCodeAsync(
        [McpToolParameter("C# code to analyze")] string code,
        [McpToolParameter("Analysis focus: quality, performance, security, maintainability, all", Required = false, DefaultValue = "all")] string focus = "all",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.CodeCannotBeEmpty)).Build();
        }

        var promptBuilder = new StringBuilder(1024);
        promptBuilder.AppendLine("你是一位资深的 C# 代码审查专家。请分析以下代码并提供详细的改进建议。");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("## 待分析代码");
        promptBuilder.AppendLine("```csharp");
        promptBuilder.AppendLine(code);
        promptBuilder.AppendLine("```");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine($"## 分析重点: {focus}");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("请提供详细的代码分析报告，包括：");
        promptBuilder.AppendLine("1. 代码质量评估");
        promptBuilder.AppendLine("2. 发现的问题和改进建议");
        promptBuilder.AppendLine("3. 最佳实践符合度");
        promptBuilder.AppendLine("4. 具体的重构建议（如有）");

        try
        {
            var result = await _queryEngine.ExecuteQueryAsync(promptBuilder.ToString(), cancellationToken).ConfigureAwait(false);
            return ToolResultBuilder.Success().WithText(result).Build();
        }
        catch (Exception ex)
        {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.CodeAnalysisFailed, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 查找 C# 代码中的错误
    /// </summary>
    [McpTool(CodeToolNameConstants.FindBugs, "Find potential bugs and issues in C# code", "code_analysis")]
    public async Task<ToolResult> FindBugsAsync(
        [McpToolParameter("C# code to check")] string code,
        [McpToolParameter("Bug severity filter: low, medium, high, critical, all", Required = false, DefaultValue = "all")] string severity = "all",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.CodeCannotBeEmpty)).Build();
        }

        var prompt = $"""
请仔细检查以下 C# 代码，找出潜在的 Bug 和问题：

## 代码
```csharp
{code}
```

## 严重程度筛选: {severity}

请按以下格式输出：
1. 发现的 Bug 列表（按严重程度排序）
2. 每个 Bug 的位置和描述
3. 修复建议
4. 预防措施
""";

        var promptBuilder = new StringBuilder(prompt.Length + 512);
        promptBuilder.AppendLine("你是一位专业的 Bug 猎人，擅长发现代码中的潜在问题。");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine(prompt);

        try
        {
            var result = await _queryEngine.ExecuteQueryAsync(promptBuilder.ToString(), cancellationToken).ConfigureAwait(false);
            return ToolResultBuilder.Success().WithText(result).Build();
        }
        catch (Exception ex)
        {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.BugFindFailed, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 优化 C# 代码
    /// </summary>
    [McpTool(CodeToolNameConstants.OptimizeCode, "Analyze and optimize C# code performance and readability", "code_analysis")]
    public async Task<ToolResult> OptimizeCodeAsync(
        [McpToolParameter("C# code to optimize")] string code,
        [McpToolParameter("Optimization target: performance, memory, readability, all", Required = false, DefaultValue = "all")] string target = "all",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.CodeCannotBeEmpty)).Build();
        }

        var prompt = $"""
请分析并优化以下 C# 代码：

## 代码
```csharp
{code}
```

## 优化目标: {target}

请提供：
1. 原始代码分析
2. 性能瓶颈识别
3. 优化后的代码
4. 优化说明和性能对比
""";

        var promptBuilder = new StringBuilder(prompt.Length + 512);
        promptBuilder.AppendLine("你是一位代码优化专家，擅长提升代码性能和可读性。");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine(prompt);

        try
        {
            var result = await _queryEngine.ExecuteQueryAsync(promptBuilder.ToString(), cancellationToken).ConfigureAwait(false);
            return ToolResultBuilder.Success().WithText(result).Build();
        }
        catch (Exception ex)
        {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.CodeOptimizationFailed, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 对 C# 代码执行安全审计
    /// </summary>
    [McpTool(CodeToolNameConstants.SecurityAudit, "Perform security audit on C# code", "code_analysis")]
    public async Task<ToolResult> SecurityAuditAsync(
        [McpToolParameter("C# code to audit")] string code,
        [McpToolParameter("Audit type: web, api, desktop, general", Required = false, DefaultValue = "general")] string audit_type = "general",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.CodeCannotBeEmpty)).Build();
        }

        var prompt = $"""
请对以下 C# 代码进行安全审计：

## 代码
```csharp
{code}
```

## 审计类型: {audit_type}

请按以下结构输出安全审计报告：
1. 总体安全评级
2. 发现的安全漏洞（按严重程度）
3. 漏洞详细说明和位置
4. 修复建议
5. 安全编码最佳实践建议
""";

        var promptBuilder = new StringBuilder(prompt.Length + 512);
        promptBuilder.AppendLine("你是一位安全审计专家，擅长识别代码中的安全漏洞。");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine(prompt);

        try
        {
            var result = await _queryEngine.ExecuteQueryAsync(promptBuilder.ToString(), cancellationToken).ConfigureAwait(false);
            return ToolResultBuilder.Success().WithText(result).Build();
        }
        catch (Exception ex)
        {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.SecurityAuditFailed, ex.Message)).Build();
        }
    }
}
