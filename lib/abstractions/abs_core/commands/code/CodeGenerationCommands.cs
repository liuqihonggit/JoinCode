namespace JoinCode.Abstractions.Commands;

public sealed class GenerateCSharpCodeCommand {
    /// <summary>获取需求描述。</summary>
    public string Description { get; }
    /// <summary>获取上下文。</summary>
    public string? Context { get; }
    /// <summary>获取框架版本。</summary>
    public string? FrameworkVersion { get; }

    /// <summary>构造生成 C# 代码命令。</summary>
    /// <param name="description">需求描述。</param>
    /// <param name="context">上下文。</param>
    /// <param name="frameworkVersion">框架版本。</param>
    public GenerateCSharpCodeCommand(string description, string? context = null, string? frameworkVersion = null) {
        Description = description ?? throw new ArgumentNullException(nameof(description));
        if (description.Length < 3) throw new ArgumentException("[ABS009] 需求描述至少需要 3 个字符", nameof(description));
        Context = context;
        FrameworkVersion = frameworkVersion;
    }
}

public sealed class GenerateUnitTestCommand {
    /// <summary>获取代码。</summary>
    public string Code { get; }
    /// <summary>获取测试框架。</summary>
    public string TestFramework { get; }
    /// <summary>获取测试数量。</summary>
    public int TestCount { get; }

    /// <summary>构造生成单元测试命令。</summary>
    /// <param name="code">代码。</param>
    /// <param name="testFramework">测试框架。</param>
    /// <param name="testCount">测试数量。</param>
    public GenerateUnitTestCommand(string code, string testFramework = "xunit", int testCount = 5) {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        if (code.Length < 10) throw new ArgumentException("[ABS010] 代码至少需要 10 个字符", nameof(code));
        TestFramework = testFramework ?? "xunit";
        TestCount = testCount > 0 ? testCount : 5;
    }
}

public sealed class GenerateApiControllerCommand {
    /// <summary>获取需求描述。</summary>
    public string Description { get; }
    /// <summary>获取模型定义。</summary>
    public string? ModelDefinition { get; }
    /// <summary>获取是否包含 CRUD 操作。</summary>
    public bool IncludeCrud { get; }
    /// <summary>获取是否包含鉴权。</summary>
    public bool IncludeAuth { get; }

    /// <summary>构造生成 API 控制器命令。</summary>
    /// <param name="description">需求描述。</param>
    /// <param name="modelDefinition">模型定义。</param>
    /// <param name="includeCrud">是否包含 CRUD。</param>
    /// <param name="includeAuth">是否包含鉴权。</param>
    public GenerateApiControllerCommand(
        string description,
        string? modelDefinition = null,
        bool includeCrud = true,
        bool includeAuth = false) {
        Description = description ?? throw new ArgumentNullException(nameof(description));
        if (description.Length < 5) throw new ArgumentException("[ABS011] 控制器描述至少需要 5 个字符", nameof(description));
        ModelDefinition = modelDefinition;
        IncludeCrud = includeCrud;
        IncludeAuth = includeAuth;
    }
}