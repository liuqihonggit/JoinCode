namespace Core.Configuration.ConfigPipeline;

/// <summary>
/// 配置加载管道共享上下文 — 在中间件各阶段间传递状态
/// </summary>
public sealed class ConfigLoadContext : IPipelineContext
{
    // === 输入 ===

    /// <summary>文件系统</summary>
    public required IFileSystem FileSystem { get; init; }

    /// <summary>项目目录</summary>
    public string? ProjectDirectory { get; init; }

    /// <summary>取消令牌</summary>
    public CancellationToken CancellationToken { get; init; }

    // === Step 1: SettingsLoadMiddleware 填充 ===

    /// <summary>合并后的设置</summary>
    public SettingsJson? Settings { get; set; }

    /// <summary>项目规则内容</summary>
    public string? ProjectRules { get; set; }

    /// <summary>外部规则文件列表</summary>
    public List<RuleFile> ExternalRules { get; set; } = [];

    // === Step 3: ConfigMapMiddleware 填充 ===

    /// <summary>映射后的工作流配置</summary>
    public WorkflowConfig Config { get; set; } = new();

    // === Step 5: ApiKeyResolveMiddleware 填充 ===

    /// <summary>解析后的 API Key</summary>
    public string? ResolvedApiKey { get; set; }

    // === 输出 ===

    /// <summary>最终配置结果</summary>
    public WorkflowConfig? Result { get; set; }

    // === IPipelineContext ===

    public bool Failed { get; set; }
    public string? ErrorMessage { get; set; }
    public void Fail(string message) { Failed = true; ErrorMessage = message; }
}
