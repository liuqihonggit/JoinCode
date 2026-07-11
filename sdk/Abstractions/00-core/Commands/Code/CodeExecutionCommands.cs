namespace JoinCode.Abstractions.Commands;

public sealed class ExecuteCSharpCodeCommand
{
    public string Code { get; }
    public int TimeoutMs { get; }
    public bool AllowExternalLibs { get; }

    public ExecuteCSharpCodeCommand(string code, int timeoutMs = 30000, bool allowExternalLibs = false)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        if (code.Length < 5) throw new ArgumentException("代码至少需要 5 个字符", nameof(code));
        if (timeoutMs is < 1000 or > 300000) throw new ArgumentOutOfRangeException(nameof(timeoutMs), "超时时间必须在 1000-300000ms 之间");
        TimeoutMs = timeoutMs;
        AllowExternalLibs = allowExternalLibs;
    }
}

public sealed class EvaluateExpressionCommand
{
    public string Expression { get; }
    public string? Variables { get; }

    public EvaluateExpressionCommand(string expression, string? variables = null)
    {
        Expression = expression ?? throw new ArgumentNullException(nameof(expression));
        if (expression.Length < 1) throw new ArgumentException("表达式至少需要 1 个字符", nameof(expression));
        Variables = variables;
    }
}

public sealed class TestCodeSnippetCommand
{
    public string Code { get; }
    public string TestInput { get; }
    public string? ExpectedOutput { get; }

    public TestCodeSnippetCommand(string code, string testInput, string? expectedOutput = null)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        if (code.Length < 5) throw new ArgumentException("代码至少需要 5 个字符", nameof(code));
        TestInput = testInput ?? throw new ArgumentNullException(nameof(testInput));
        ExpectedOutput = expectedOutput;
    }
}
