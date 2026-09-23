namespace JccAuditCli;

/// <summary>
/// DI 循环依赖信息
/// </summary>
public sealed class DiCycleInfo {
    /// <summary>获取循环路径。</summary>
    public string[] Path { get; }
    /// <summary>获取循环边集合。</summary>
    public (string From, string To, string? File, int? Line)[] Edges { get; }
    /// <summary>获取严重程度。</summary>
    public int Severity { get; }

    /// <summary>构造 DI 循环依赖信息。</summary>
    /// <param name="path">循环路径。</param>
    /// <param name="edges">循环边集合。</param>
    /// <param name="severity">严重程度。</param>
    public DiCycleInfo(string[] path, (string From, string To, string? File, int? Line)[] edges, int severity) {
        Path = path;
        Edges = edges;
        Severity = severity;
    }
}

/// <summary>
/// 服务注册信息
/// </summary>
public sealed class ServiceRegistration {
    /// <summary>获取服务类型。</summary>
    public string ServiceType { get; }
    /// <summary>获取实现类型。</summary>
    public string ImplementationType { get; }
    /// <summary>获取生命周期。</summary>
    public string Lifetime { get; }

    /// <summary>构造服务注册信息。</summary>
    /// <param name="serviceType">服务类型。</param>
    /// <param name="implementationType">实现类型。</param>
    /// <param name="lifetime">生命周期。</param>
    public ServiceRegistration(string serviceType, string implementationType, string lifetime) {
        ServiceType = serviceType;
        ImplementationType = implementationType;
        Lifetime = lifetime;
    }
}

/// <summary>
/// 构造函数依赖信息
/// </summary>
public sealed class ConstructorDependency {
    /// <summary>获取类名。</summary>
    public string ClassName { get; }
    /// <summary>获取依赖类型。</summary>
    public string DependencyType { get; }
    /// <summary>获取文件路径。</summary>
    public string? FilePath { get; }
    /// <summary>获取行号。</summary>
    public int? LineNumber { get; }
    /// <summary>获取是否可选。</summary>
    public bool IsOptional { get; }

    /// <summary>构造函数依赖信息。</summary>
    /// <param name="className">类名。</param>
    /// <param name="dependencyType">依赖类型。</param>
    /// <param name="filePath">文件路径。</param>
    /// <param name="lineNumber">行号。</param>
    /// <param name="isOptional">是否可选。</param>
    public ConstructorDependency(string className, string dependencyType, string? filePath, int? lineNumber, bool isOptional) {
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
public sealed class ConstructorParamInfo {
    /// <summary>获取类名。</summary>
    public string ClassName { get; }
    /// <summary>获取文件路径。</summary>
    public string FilePath { get; }
    /// <summary>获取行号。</summary>
    public int LineNumber { get; }
    /// <summary>获取参数数量。</summary>
    public int ParameterCount { get; }
    /// <summary>获取参数类型列表。</summary>
    public List<string> ParameterTypes { get; }
    /// <summary>获取构造函数签名。</summary>
    public string ConstructorSignature { get; }

    /// <summary>构造函数参数信息。</summary>
    /// <param name="className">类名。</param>
    /// <param name="filePath">文件路径。</param>
    /// <param name="lineNumber">行号。</param>
    /// <param name="parameterCount">参数数量。</param>
    /// <param name="parameterTypes">参数类型列表。</param>
    /// <param name="constructorSignature">构造函数签名。</param>
    public ConstructorParamInfo(string className, string filePath, int lineNumber,
        int parameterCount, List<string> parameterTypes, string constructorSignature) {
        ClassName = className;
        FilePath = filePath;
        LineNumber = lineNumber;
        ParameterCount = parameterCount;
        ParameterTypes = parameterTypes;
        ConstructorSignature = constructorSignature;
    }
}