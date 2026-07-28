namespace JoinCode.Abstractions.Commands;

public sealed class GenerateCSharpCodeCommand
{
    public string Description { get; }
    public string? Context { get; }
    public string? FrameworkVersion { get; }

    public GenerateCSharpCodeCommand(string description, string? context = null, string? frameworkVersion = null)
    {
        Description = description ?? throw new ArgumentNullException(nameof(description));
        if (description.Length < 3) throw new ArgumentException("需求描述至少需要 3 个字符", nameof(description));
        Context = context;
        FrameworkVersion = frameworkVersion;
    }
}

public sealed class GenerateUnitTestCommand
{
    public string Code { get; }
    public string TestFramework { get; }
    public int TestCount { get; }

    public GenerateUnitTestCommand(string code, string testFramework = "xunit", int testCount = 5)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        if (code.Length < 10) throw new ArgumentException("代码至少需要 10 个字符", nameof(code));
        TestFramework = testFramework ?? "xunit";
        TestCount = testCount > 0 ? testCount : 5;
    }
}

public sealed class GenerateApiControllerCommand
{
    public string Description { get; }
    public string? ModelDefinition { get; }
    public bool IncludeCrud { get; }
    public bool IncludeAuth { get; }

    public GenerateApiControllerCommand(
        string description,
        string? modelDefinition = null,
        bool includeCrud = true,
        bool includeAuth = false)
    {
        Description = description ?? throw new ArgumentNullException(nameof(description));
        if (description.Length < 5) throw new ArgumentException("控制器描述至少需要 5 个字符", nameof(description));
        ModelDefinition = modelDefinition;
        IncludeCrud = includeCrud;
        IncludeAuth = includeAuth;
    }
}
