namespace JoinCode.Abstractions.Commands;

/// <summary>执行 C# 代码命令。</summary>
public sealed class ExecuteCSharpCodeCommand {
    /// <summary>获取要执行的代码。</summary>
    public string Code { get; }
    /// <summary>获取超时时间（毫秒）。</summary>
    public int TimeoutMs { get; }
    /// <summary>获取是否允许使用外部库。</summary>
    public bool AllowExternalLibs { get; }

    /// <summary>
    /// 构造执行 C# 代码命令。
    /// </summary>
    /// <param name="code">要执行的代码。</param>
    /// <param name="timeoutMs">超时时间（毫秒）。</param>
    /// <param name="allowExternalLibs">是否允许使用外部库。</param>
    public ExecuteCSharpCodeCommand(string code, int timeoutMs = 30000, bool allowExternalLibs = false) {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        if (code.Length < 5) throw new ArgumentException("[ABS005] 代码至少需要 5 个字符", nameof(code));
        if (timeoutMs is < 1000 or > 300000) throw new ArgumentOutOfRangeException(nameof(timeoutMs), "[ABS006] 超时时间必须在 1000-300000ms 之间");
        TimeoutMs = timeoutMs;
        AllowExternalLibs = allowExternalLibs;
    }
}

/// <summary>求值表达式命令。</summary>
public sealed class EvaluateExpressionCommand {
    /// <summary>获取表达式字符串。</summary>
    public string Expression { get; }
    /// <summary>获取变量定义。</summary>
    public string? Variables { get; }

    /// <summary>
    /// 构造求值表达式命令。
    /// </summary>
    /// <param name="expression">表达式字符串。</param>
    /// <param name="variables">变量定义。</param>
    public EvaluateExpressionCommand(string expression, string? variables = null) {
        Expression = expression ?? throw new ArgumentNullException(nameof(expression));
        if (expression.Length < 1) throw new ArgumentException("[ABS007] 表达式至少需要 1 个字符", nameof(expression));
        Variables = variables;
    }
}

/// <summary>测试代码片段命令。</summary>
public sealed class TestCodeSnippetCommand {
    /// <summary>获取要测试的代码。</summary>
    public string Code { get; }
    /// <summary>获取测试输入。</summary>
    public string TestInput { get; }
    /// <summary>获取期望输出。</summary>
    public string? ExpectedOutput { get; }

    /// <summary>
    /// 构造测试代码片段命令。
    /// </summary>
    /// <param name="code">要测试的代码。</param>
    /// <param name="testInput">测试输入。</param>
    /// <param name="expectedOutput">期望输出。</param>
    public TestCodeSnippetCommand(string code, string testInput, string? expectedOutput = null) {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        if (code.Length < 5) throw new ArgumentException("[ABS008] 代码至少需要 5 个字符", nameof(code));
        TestInput = testInput ?? throw new ArgumentNullException(nameof(testInput));
        ExpectedOutput = expectedOutput;
    }
}
