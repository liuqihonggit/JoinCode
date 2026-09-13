namespace JccAuditCli;

/// <summary>
/// DI 循环依赖信息
/// </summary>
public sealed class DiCycleInfo
{
    public string[] Path { get; }
    public (string From, string To, string? File, int? Line)[] Edges { get; }
    public int Severity { get; }

    public DiCycleInfo(string[] path, (string From, string To, string? File, int? Line)[] edges, int severity)
    {
        Path = path;
        Edges = edges;
        Severity = severity;
    }
}

/// <summary>
/// 服务注册信息
/// </summary>
public sealed class ServiceRegistration
{
    public string ServiceType { get; }
    public string ImplementationType { get; }
    public string Lifetime { get; }

    public ServiceRegistration(string serviceType, string implementationType, string lifetime)
    {
        ServiceType = serviceType;
        ImplementationType = implementationType;
        Lifetime = lifetime;
    }
}

/// <summary>
/// 构造函数依赖信息
/// </summary>
public sealed class ConstructorDependency
{
    public string ClassName { get; }
    public string DependencyType { get; }
    public string? FilePath { get; }
    public int? LineNumber { get; }
    public bool IsOptional { get; }

    public ConstructorDependency(string className, string dependencyType, string? filePath, int? lineNumber, bool isOptional)
    {
        ClassName = className;
        DependencyType = dependencyType;
        FilePath = filePath;
        LineNumber = lineNumber;
        IsOptional = isOptional;
    }
}

/// <summary>
/// 构造函数参数计数信息 — 用于检测参数过多的构造函数（可能需要中间件模式重构）
/// </summary>
public sealed class ConstructorParamInfo
{
    public string ClassName { get; }
    public string FilePath { get; }
    public int LineNumber { get; }
    public int ParameterCount { get; }
    public List<string> ParameterTypes { get; }
    public string ConstructorSignature { get; }

    public ConstructorParamInfo(string className, string filePath, int lineNumber,
        int parameterCount, List<string> parameterTypes, string constructorSignature)
    {
        ClassName = className;
        FilePath = filePath;
        LineNumber = lineNumber;
        ParameterCount = parameterCount;
        ParameterTypes = parameterTypes;
        ConstructorSignature = constructorSignature;
    }
}
