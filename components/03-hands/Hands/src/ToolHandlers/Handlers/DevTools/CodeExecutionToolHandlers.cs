


namespace Tools.Handlers;

/// <summary>
/// 代码执行工具处理器 - 提供安全的 C# 代码执行、表达式计算、代码片段测试等功能
/// </summary>
[McpToolHandler(ToolCategory.CodeExecution)]
public class CodeExecutionToolHandlers
{
    private readonly ICodeSandboxService _codeSandboxService;
    private readonly ICodeSecurityValidator _codeSecurityValidator;
    private readonly ITelemetryService? _telemetryService;

    public CodeExecutionToolHandlers(
        ICodeSandboxService codeSandboxService,
        ICodeSecurityValidator codeSecurityValidator,
        ITelemetryService? telemetryService = null)
    {
        _codeSandboxService = codeSandboxService ?? throw new ArgumentNullException(nameof(codeSandboxService));
        _codeSecurityValidator = codeSecurityValidator ?? throw new ArgumentNullException(nameof(codeSecurityValidator));
        _telemetryService = telemetryService;
    }

    /// <summary>
    /// 安全地执行 C# 代码并返回结果
    /// </summary>
    [McpTool(CodeToolNameConstants.ExecuteCsharpCode, "Execute C# code in sandbox environment", "code_execution")]
    public async Task<ToolResult> ExecuteCSharpCodeAsync(
        [McpToolParameter("C# code to execute")] string code,
        [McpToolParameter("Execution timeout in milliseconds, defaults to 30000", Required = false, DefaultValue = "30000")] int timeout_ms = 30000,
        [McpToolParameter("Allow external libraries", Required = false, DefaultValue = "false")] bool allow_external_libs = false,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidationHelper.CombineErrors(
            ValidationHelper.ValidateRequired(code, "code"),
            ValidationHelper.ValidateRange(timeout_ms, 1, 300000, "timeout_ms"));
        if (validationError != null)
        {
            return ToolResultBuilder.Error().WithText(validationError).Build();
        }

        // 安全检查
        var securityCheck = _codeSecurityValidator.Validate(code, allow_external_libs);
        if (!securityCheck.IsValid)
        {
            RecordCodeExecutionMetrics("execute", "security_fail");
            return ToolResultBuilder.Error()
                .WithText($"Security warning: {securityCheck.Message}")
                .Build();
        }

        try
        {
            var result = await _codeSandboxService.ExecuteAsync(code, timeout_ms, cancellationToken).ConfigureAwait(false);
            RecordCodeExecutionMetrics("execute", "ok");
            return ToolResultBuilder.Success().WithText(result).Build();
        }
        catch (OperationCanceledException)
        {
            RecordCodeExecutionMetrics("execute", "cancelled");
            return ToolResultBuilder.Error().WithText("Code execution cancelled").Build();
        }
        catch (TimeoutException)
        {
            RecordCodeExecutionMetrics("execute", "timeout");
            return ToolResultBuilder.Error().WithText($"Code execution timed out (exceeded {timeout_ms}ms)").Build();
        }
        catch (Exception ex)
        {
            RecordCodeExecutionMetrics("execute", "error");
            return ToolResultBuilder.Error().WithText($"Code execution failed: {ex.Message}").Build();
        }
    }

    /// <summary>
    /// 计算 C# 表达式
    /// </summary>
    [McpTool(CodeToolNameConstants.EvaluateExpression, "Evaluate a simple C# expression", "code_execution")]
    public async Task<ToolResult> EvaluateExpressionAsync(
        [McpToolParameter("C# expression to evaluate, e.g. '1 + 2 * 3' or 'DateTime.Now.ToString()'")] string expression,
        [McpToolParameter("Variable definitions in JSON format (optional)", Required = false)] string? variables = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return ToolResultBuilder.Error().WithText("Expression cannot be empty").Build();
        }

        try
        {
            var result = await _codeSandboxService.EvaluateExpressionAsync(expression, variables, cancellationToken).ConfigureAwait(false);
            RecordCodeExecutionMetrics("evaluate", "ok");
            return ToolResultBuilder.Success().WithText(result).Build();
        }
        catch (Exception ex)
        {
            RecordCodeExecutionMetrics("evaluate", "error");
            return ToolResultBuilder.Error().WithText($"Expression evaluation failed: {ex.Message}").Build();
        }
    }

    /// <summary>
    /// 使用示例输入测试代码片段
    /// </summary>
    [McpTool(CodeToolNameConstants.TestCodeSnippet, "Test a code snippet with sample input", "code_execution")]
    public async Task<ToolResult> TestCodeSnippetAsync(
        [McpToolParameter("C# code snippet to test")] string code,
        [McpToolParameter("Sample input in JSON format")] string test_input,
        [McpToolParameter("Expected output (optional)", Required = false)] string? expected_output = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return ToolResultBuilder.Error().WithText("Code cannot be empty").Build();
        }

        if (string.IsNullOrWhiteSpace(test_input))
        {
            return ToolResultBuilder.Error().WithText("Test input cannot be empty").Build();
        }

        // 安全检查
        var securityCheck = _codeSecurityValidator.Validate(code, false);
        if (!securityCheck.IsValid)
        {
            RecordCodeExecutionMetrics("test_snippet", "security_fail");
            return ToolResultBuilder.Error()
                .WithText($"Security warning: {securityCheck.Message}")
                .Build();
        }

        try
        {
            var result = await _codeSandboxService.ExecuteAsync(
                BuildTestCode(code, test_input),
                30000,
                cancellationToken).ConfigureAwait(false);

            var responseBuilder = new System.Text.StringBuilder(512);
            responseBuilder.AppendLine("## Test Results");
            responseBuilder.AppendLine();
            responseBuilder.AppendLine("### Output");
            responseBuilder.AppendLine("```");
            responseBuilder.AppendLine(result);
            responseBuilder.AppendLine("```");

            if (!string.IsNullOrWhiteSpace(expected_output))
            {
                var match = result.Trim() == expected_output.Trim();
                responseBuilder.AppendLine();
                responseBuilder.AppendLine($"### Expected output match: {(match ? $"{StatusSymbol.Tick.ToValue()} Pass" : $"{StatusSymbol.Cross.ToValue()} Fail")}");
                if (!match)
                {
                    responseBuilder.AppendLine("```");
                    responseBuilder.AppendLine(expected_output);
                    responseBuilder.AppendLine("```");
                }
            }

            RecordCodeExecutionMetrics("test_snippet", "ok");
            return ToolResultBuilder.Success().WithText(responseBuilder.ToString()).Build();
        }
        catch (Exception ex)
        {
            RecordCodeExecutionMetrics("test_snippet", "error");
            return ToolResultBuilder.Error().WithText($"Code test failed: {ex.Message}").Build();
        }
    }

    private void RecordCodeExecutionMetrics(string operation, string result)
        => _telemetryService?.RecordCount("code.execution.count", new Dictionary<string, string> { ["operation"] = operation, ["result"] = result }, description: "Code execution count");

    private static string BuildTestCode(string code, string testInput)
    {
        var codeBuilder = new System.Text.StringBuilder(1024);
        codeBuilder.AppendLine("using System;");
        codeBuilder.AppendLine("using System.Linq;");
        codeBuilder.AppendLine("using System.Collections.Generic;");
        codeBuilder.AppendLine("using System.Text.Json;");
        codeBuilder.AppendLine();
        codeBuilder.AppendLine("public class Program");
        codeBuilder.AppendLine("{");
        codeBuilder.AppendLine("    public static void Main()");
        codeBuilder.AppendLine("    {");
        codeBuilder.AppendLine("        try");
        codeBuilder.AppendLine("        {");
        codeBuilder.AppendLine($"            var testInput = \"{EscapeString(testInput)}\";");
        codeBuilder.AppendLine();
        codeBuilder.AppendLine("            // 用户代码开始");
        codeBuilder.AppendLine(code);
        codeBuilder.AppendLine("            // 用户代码结束");
        codeBuilder.AppendLine("        }");
        codeBuilder.AppendLine("        catch (Exception ex)");
        codeBuilder.AppendLine("        {");
        codeBuilder.AppendLine("            Console.WriteLine($\"Error: {ex.GetType().Name}: {ex.Message}\");");
        codeBuilder.AppendLine("            Console.WriteLine($\"StackTrace: {ex.StackTrace}\");");
        codeBuilder.AppendLine("        }");
        codeBuilder.AppendLine("    }");
        codeBuilder.AppendLine("}");

        return codeBuilder.ToString();
    }

    private static string EscapeString(string input)
    {
        return input
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}
