


namespace McpToolHandlers;

/// <summary>
/// 代码生成工具处理器 - 提供 C# 代码生成、单元测试生成、API 控制器生成等功能
/// </summary>
[McpToolHandler(ToolCategory.CodeGeneration, Optional = true)]
public class CodeGenerationToolHandlers
{
    private readonly IQueryEngine _queryEngine;

    public CodeGenerationToolHandlers(IQueryEngine queryEngine)
    {
        _queryEngine = queryEngine ?? throw new ArgumentNullException(nameof(queryEngine));
    }

    /// <summary>
    /// 根据描述生成 C# 代码
    /// </summary>
    [McpTool(CodeToolNameConstants.GenerateCsharpCode, "Generate C# code from description", "code_generation")]
    public async Task<ToolResult> GenerateCSharpCodeAsync(
        [McpToolParameter("Code requirement description")] string description,
        [McpToolParameter("Code context or related code snippets", Required = false)] string? context = null,
        [McpToolParameter("Target framework version, e.g. net8.0, net10.0", Required = false)] string? framework_version = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.DescriptionCannotBeEmpty)).Build();
        }

        var promptBuilder = new StringBuilder(1024);
        promptBuilder.AppendLine("你是一位专业的 C# 开发专家。请根据以下需求生成高质量的 C# 代码。");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("## 需求描述");
        promptBuilder.AppendLine(description);

        if (!string.IsNullOrWhiteSpace(framework_version))
        {
            promptBuilder.AppendLine();
            promptBuilder.AppendLine($"## 目标框架: {framework_version}");
        }

        if (!string.IsNullOrWhiteSpace(context))
        {
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("## 上下文代码");
            promptBuilder.AppendLine("```csharp");
            promptBuilder.AppendLine(context);
            promptBuilder.AppendLine("```");
        }

        promptBuilder.AppendLine();
        promptBuilder.AppendLine("请生成满足上述需求的 C# 代码。代码要求：");
        promptBuilder.AppendLine("1. 遵循 .NET 最佳实践");
        promptBuilder.AppendLine("2. 包含必要的 XML 文档注释");
        promptBuilder.AppendLine("3. 实现适当的错误处理");
        promptBuilder.AppendLine("4. 使用现代 C# 特性");
        promptBuilder.AppendLine("5. 确保代码可编译通过");

        try
        {
            var result = await _queryEngine.ExecuteQueryAsync(promptBuilder.ToString(), cancellationToken).ConfigureAwait(false);
            return ToolResultBuilder.Success().WithText(result).Build();
        }
        catch (Exception ex)
        {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.CodeGenerationFailed, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 为现有代码生成单元测试
    /// </summary>
    [McpTool(CodeToolNameConstants.GenerateUnitTest, "Generate unit tests for existing C# code", "code_generation")]
    public async Task<ToolResult> GenerateUnitTestAsync(
        [McpToolParameter("C# code to test")] string code,
        [McpToolParameter("Test framework, e.g. xunit, nunit, mstest", Required = false, DefaultValue = "xunit")] string test_framework = "xunit",
        [McpToolParameter("Number of tests to generate", Required = false, DefaultValue = "5")] int test_count = 5,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.CodeCannotBeEmpty)).Build();
        }

        var prompt = $"""
请为以下 C# 代码生成 {test_count} 个单元测试：

## 代码
```csharp
{code}
```

## 测试框架: {test_framework}

额外要求：
1. 包含正向测试和边界情况测试
2. 使用 Arrange-Act-Assert 模式
3. 测试方法命名清晰描述测试场景
4. 包含必要的 Mock 和依赖注入设置
""";

        var promptBuilder = new StringBuilder(prompt.Length + 256);
        promptBuilder.AppendLine("你是一位单元测试专家，擅长编写高质量的测试代码。");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine(prompt);

        try
        {
            var result = await _queryEngine.ExecuteQueryAsync(promptBuilder.ToString(), cancellationToken).ConfigureAwait(false);
            return ToolResultBuilder.Success().WithText(result).Build();
        }
        catch (Exception ex)
        {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.UnitTestGenerationFailed, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 生成 ASP.NET Core API 控制器
    /// </summary>
    [McpTool(CodeToolNameConstants.GenerateApiController, "Generate ASP.NET Core API controller", "code_generation")]
    public async Task<ToolResult> GenerateApiControllerAsync(
        [McpToolParameter("Controller requirement description")] string description,
        [McpToolParameter("Entity/model class definition", Required = false)] string? model_definition = null,
        [McpToolParameter("Whether to include CRUD operations", Required = false, DefaultValue = "true")] bool include_crud = true,
        [McpToolParameter("Whether to include authentication", Required = false, DefaultValue = "false")] bool include_auth = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.DescriptionCannotBeEmpty)).Build();
        }

        var promptBuilder = new StringBuilder(1024);
        promptBuilder.AppendLine("你是一位 ASP.NET Core 开发专家。请根据以下需求生成 API 控制器代码。");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("## 需求描述");
        promptBuilder.AppendLine(description);

        if (!string.IsNullOrWhiteSpace(model_definition))
        {
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("## 实体/模型定义");
            promptBuilder.AppendLine("```csharp");
            promptBuilder.AppendLine(model_definition);
            promptBuilder.AppendLine("```");
        }

        promptBuilder.AppendLine();
        promptBuilder.AppendLine("## 控制器要求");
        promptBuilder.AppendLine($"- 包含 CRUD 操作: {(include_crud ? "是" : "否")}");
        promptBuilder.AppendLine($"- 包含身份验证: {(include_auth ? "是" : "否")}");
        promptBuilder.AppendLine("- 使用标准 RESTful API 设计");
        promptBuilder.AppendLine("- 包含适当的 HTTP 状态码返回");
        promptBuilder.AppendLine("- 包含 XML 文档注释用于 Swagger");
        promptBuilder.AppendLine("- 实现适当的输入验证");
        promptBuilder.AppendLine("- 包含异常处理中间件");

        try
        {
            var result = await _queryEngine.ExecuteQueryAsync(promptBuilder.ToString(), cancellationToken).ConfigureAwait(false);
            return ToolResultBuilder.Success().WithText(result).Build();
        }
        catch (Exception ex)
        {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.ApiControllerGenerationFailed, ex.Message)).Build();
        }
    }
}
