
namespace Core.Configuration;

/// <summary>
/// 权限检查配置
/// </summary>
public class PermissionConfig
{
    /// <summary>
    /// 自动批准的工具列表
    /// </summary>
    public Dictionary<string, ToolPermissionRule> AutoApprovedTools { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 自动拒绝的工具列表
    /// </summary>
    public Dictionary<string, ToolPermissionRule> AutoRejectedTools { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 需要用户确认的工具列表 — 对齐 TS 版 ask 规则
    /// 支持 RuleContent 细粒度匹配（如 domain:example.com）
    /// </summary>
    public List<ToolPermissionRule> AskRules { get; set; } = new();

    /// <summary>
    /// 危险操作模式定义
    /// </summary>
    public List<OperationPattern> DangerousOperationPatterns { get; set; } = new();

    /// <summary>
    /// 写操作模式定义
    /// </summary>
    public List<OperationPattern> WriteOperationPatterns { get; set; } = new();

    /// <summary>
    /// 读操作模式定义
    /// </summary>
    public List<OperationPattern> ReadOperationPatterns { get; set; } = new();

    /// <summary>
    /// Shell操作模式定义
    /// </summary>
    public List<OperationPattern> ShellOperationPatterns { get; set; } = new();

    /// <summary>
    /// 敏感路径模式
    /// </summary>
    public List<SensitivePathPattern> SensitivePathPatterns { get; set; } = new();

    /// <summary>
    /// 危险命令模式
    /// </summary>
    public List<DangerousCommandPattern> DangerousCommandPatterns { get; set; } = new();

    /// <summary>
    /// 额外工作目录 — 对齐 TS additionalWorkingDirectories
    /// 在这些目录内的读取操作自动允许
    /// </summary>
    public List<string> AdditionalDirectories { get; set; } = new();

    /// <summary>
    /// 工具白名单/黑名单覆盖 — 增量合并到 AgentToolRestrictions 硬编码默认值
    /// 键为模式名（"auto"/"plan"/"ask"），值为允许/拒绝的工具列表
    /// </summary>
    public Dictionary<string, ToolOverrideEntry> ToolOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 创建默认配置
    /// </summary>
    public static PermissionConfig CreateDefault()
    {
        return new PermissionConfig
        {
            AutoApprovedTools = new Dictionary<string, ToolPermissionRule>(StringComparer.OrdinalIgnoreCase)
            {
                [FileToolNameConstants.FileRead] = new ToolPermissionRule { ToolName = FileToolNameConstants.FileRead, Description = "Read file" },
                ["file_list"] = new ToolPermissionRule { ToolName = "file_list", Description = "List files" },
                [FileToolNameConstants.DirectoryList] = new ToolPermissionRule { ToolName = FileToolNameConstants.DirectoryList, Description = "List directory" },
                [SearchToolNameConstants.Glob] = new ToolPermissionRule { ToolName = SearchToolNameConstants.Glob, Description = "File pattern matching" },
                [SearchToolNameConstants.Grep] = new ToolPermissionRule { ToolName = SearchToolNameConstants.Grep, Description = "Text search" },
                // WebFetch 不在 AutoApprovedTools 中 — 对齐 TS 版: WebFetch 需要域名级权限检查
                // 预批准域名由 PreapprovedDomains 管理，用户可通过 /allowed-tools 添加域名白名单
                // WebSearch 只读操作，自动批准
                [WebToolNameConstants.WebSearch] = new ToolPermissionRule { ToolName = WebToolNameConstants.WebSearch, Description = "Web search" },
                [TaskToolNameConstants.TaskList] = new ToolPermissionRule { ToolName = TaskToolNameConstants.TaskList, Description = "List tasks" },
                [TaskToolNameConstants.TaskGet] = new ToolPermissionRule { ToolName = TaskToolNameConstants.TaskGet, Description = "Get task" },
                [SystemToolNameConstants.TaskOutput] = new ToolPermissionRule { ToolName = SystemToolNameConstants.TaskOutput, Description = "Get task output" }
            },
            DangerousOperationPatterns =
            [
                new OperationPattern { Pattern = OperationTypeConstants.Delete, PatternType = PatternType.Contains, Description = "删除操作" },
                new OperationPattern { Pattern = OperationTypeConstants.Bash, PatternType = PatternType.Contains, Description = "Bash命令" },
                new OperationPattern { Pattern = OperationTypeConstants.Shell, PatternType = PatternType.Contains, Description = "Shell命令" }
            ],
            WriteOperationPatterns =
            [
                new OperationPattern { Pattern = OperationTypeConstants.Write, PatternType = PatternType.Contains, Description = "写入操作" },
                new OperationPattern { Pattern = OperationTypeConstants.Edit, PatternType = PatternType.Contains, Description = "编辑操作" },
                new OperationPattern { Pattern = OperationTypeConstants.Create, PatternType = PatternType.Contains, Description = "创建操作" },
                new OperationPattern { Pattern = OperationTypeConstants.Delete, PatternType = PatternType.Contains, Description = "删除操作" }
            ],
            ReadOperationPatterns =
            [
                new OperationPattern { Pattern = OperationTypeConstants.Read, PatternType = PatternType.Contains, Description = "读取操作" },
                new OperationPattern { Pattern = OperationTypeConstants.List, PatternType = PatternType.Contains, Description = "列出操作" },
                new OperationPattern { Pattern = OperationTypeConstants.Get, PatternType = PatternType.Contains, Description = "获取操作" },
                new OperationPattern { Pattern = OperationTypeConstants.Search, PatternType = PatternType.Contains, Description = "搜索操作" },
                new OperationPattern { Pattern = OperationTypeConstants.Glob, PatternType = PatternType.Contains, Description = "模式匹配" },
                new OperationPattern { Pattern = OperationTypeConstants.Grep, PatternType = PatternType.Contains, Description = "文本搜索" }
            ],
            ShellOperationPatterns =
            [
                new OperationPattern { Pattern = OperationTypeConstants.Bash, PatternType = PatternType.Contains, Description = "Bash命令" },
                new OperationPattern { Pattern = OperationTypeConstants.Shell, PatternType = PatternType.Contains, Description = "Shell命令" },
                new OperationPattern { Pattern = OperationTypeConstants.Execute, PatternType = PatternType.Contains, Description = "执行命令" },
                new OperationPattern { Pattern = OperationTypeConstants.Run, PatternType = PatternType.Contains, Description = "运行命令" }
            ],
            SensitivePathPatterns =
            [
                new SensitivePathPattern { Path = "{Windows}", PathType = PathType.SpecialFolder, Description = "Windows目录" },
                new SensitivePathPattern { Path = "{System}", PathType = PathType.SpecialFolder, Description = "系统目录" },
                new SensitivePathPattern { Path = "{SystemX86}", PathType = PathType.SpecialFolder, Description = "系统目录(x86)" },
                new SensitivePathPattern { Path = ".git\\config", PathType = PathType.Contains, Description = "Git配置" },
                new SensitivePathPattern { Path = ".ssh\\", PathType = PathType.Contains, Description = "SSH目录" },
                new SensitivePathPattern { Path = "/etc/", PathType = PathType.Contains, Description = "系统配置目录" }
            ],
            DangerousCommandPatterns =
            [
                new DangerousCommandPattern { Pattern = "rm -rf /", Description = "删除根目录" },
                new DangerousCommandPattern { Pattern = "del /f /s /q c:", Description = "删除C盘" },
                new DangerousCommandPattern { Pattern = "format", Description = "格式化" },
                new DangerousCommandPattern { Pattern = "fdisk", Description = "分区操作" },
                new DangerousCommandPattern { Pattern = "mkfs", Description = "创建文件系统" },
                new DangerousCommandPattern { Pattern = "dd if=", Description = "磁盘复制" },
                new DangerousCommandPattern { Pattern = ":(){ :|:& };:", Description = "Fork炸弹" },
                new DangerousCommandPattern { Pattern = "shutdown", Description = "关机" },
                new DangerousCommandPattern { Pattern = "restart", Description = "重启" },
                new DangerousCommandPattern { Pattern = "wmic", Description = "WMI命令" },
                new DangerousCommandPattern { Pattern = "reg delete", Description = "删除注册表" },
                new DangerousCommandPattern { Pattern = "net user", Description = "用户管理" },
                new DangerousCommandPattern { Pattern = "net localgroup", Description = "用户组管理" }
            ]
        };
    }
}

/// <summary>
/// 工具权限规则
/// 对齐 TS 版 PermissionRuleValue — 支持 ToolName 级和 RuleContent 级（如 domain:xxx.com）匹配
/// </summary>
public class ToolPermissionRule : DescribedRule
{
    /// <summary>
    /// 工具名称（兼容旧配置，委托到 Value）
    /// </summary>
    public string ToolName { get => Value; set => Value = value; }

    /// <summary>
    /// 规则内容 — 用于细粒度匹配，格式为 "domain:hostname"
    /// 对齐 TS 版 ruleContent — web_fetch 工具使用 "domain:example.com" 格式
    /// 为空时仅匹配 ToolName
    /// </summary>
    public string? RuleContent { get; set; }
}

/// <summary>
/// 操作模式定义
/// </summary>
public class OperationPattern : DescribedRule
{
    /// <summary>
    /// 匹配模式（兼容旧配置，委托到 Value）
    /// </summary>
    public string Pattern { get => Value; set => Value = value; }

    public PatternType PatternType { get; set; } = PatternType.Contains;
}

/// <summary>
/// 敏感路径模式
/// </summary>
public class SensitivePathPattern : DescribedRule
{
    /// <summary>
    /// 路径（兼容旧配置，委托到 Value）
    /// </summary>
    public string Path { get => Value; set => Value = value; }

    public PathType PathType { get; set; } = PathType.Contains;
}

/// <summary>
/// 危险命令模式
/// </summary>
public class DangerousCommandPattern : DescribedRule
{
    /// <summary>
    /// 匹配模式（兼容旧配置，委托到 Value）
    /// </summary>
    public string Pattern { get => Value; set => Value = value; }
}

/// <summary>
/// 模式匹配类型
/// </summary>
public enum PatternType
{
    [EnumValue("contains")] Contains,
    [EnumValue("startsWith")] StartsWith,
    [EnumValue("endsWith")] EndsWith,
    [EnumValue("exact")] Exact,
    [EnumValue("regex")] Regex
}

/// <summary>
/// 路径类型
/// </summary>
public enum PathType
{
    [EnumValue("contains")] Contains,
    [EnumValue("startsWith")] StartsWith,
    [EnumValue("specialFolder")] SpecialFolder
}

#region Builders

/// <summary>
/// 权限配置构建器 - 支持链式配置
/// </summary>
public sealed class PermissionConfigBuilder
{
    private readonly Dictionary<string, ToolPermissionRule> _autoApprovedTools = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ToolPermissionRule> _autoRejectedTools = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OperationPattern> _dangerousOperationPatterns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OperationPattern> _writeOperationPatterns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OperationPattern> _readOperationPatterns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OperationPattern> _shellOperationPatterns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SensitivePathPattern> _sensitivePathPatterns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DangerousCommandPattern> _dangerousCommandPatterns = new(StringComparer.OrdinalIgnoreCase);

    private PermissionConfigBuilder()
    {
    }

    /// <summary>
    /// 创建新的构建器
    /// </summary>
    public static PermissionConfigBuilder Create() => new();

    /// <summary>
    /// 从默认配置开始
    /// </summary>
    public static PermissionConfigBuilder CreateFromDefault()
    {
        var builder = new PermissionConfigBuilder();
        var defaultConfig = PermissionConfig.CreateDefault();
        
        foreach (var kvp in defaultConfig.AutoApprovedTools)
            builder._autoApprovedTools[kvp.Key] = kvp.Value;
        foreach (var p in defaultConfig.DangerousOperationPatterns)
            builder._dangerousOperationPatterns[p.Pattern] = p;
        foreach (var p in defaultConfig.WriteOperationPatterns)
            builder._writeOperationPatterns[p.Pattern] = p;
        foreach (var p in defaultConfig.ReadOperationPatterns)
            builder._readOperationPatterns[p.Pattern] = p;
        foreach (var p in defaultConfig.ShellOperationPatterns)
            builder._shellOperationPatterns[p.Pattern] = p;
        foreach (var p in defaultConfig.SensitivePathPatterns)
            builder._sensitivePathPatterns[p.Path] = p;
        foreach (var p in defaultConfig.DangerousCommandPatterns)
            builder._dangerousCommandPatterns[p.Pattern] = p;
        
        return builder;
    }

    /// <summary>
    /// 添加自动批准的工具
    /// </summary>
    public PermissionConfigBuilder AddAutoApprovedTool(string toolName, string description = "")
    {
        _autoApprovedTools[toolName] = new ToolPermissionRule { ToolName = toolName, Description = description };
        return this;
    }

    /// <summary>
    /// 添加自动拒绝的工具
    /// </summary>
    public PermissionConfigBuilder AddAutoRejectedTool(string toolName, string description = "")
    {
        _autoRejectedTools[toolName] = new ToolPermissionRule { ToolName = toolName, Description = description };
        return this;
    }

    /// <summary>
    /// 添加危险操作模式
    /// </summary>
    public PermissionConfigBuilder AddDangerousOperation(string pattern, PatternType patternType, string description = "")
    {
        _dangerousOperationPatterns[pattern] = new OperationPattern { Pattern = pattern, PatternType = patternType, Description = description };
        return this;
    }

    /// <summary>
    /// 添加写操作模式
    /// </summary>
    public PermissionConfigBuilder AddWriteOperation(string pattern, PatternType patternType, string description = "")
    {
        _writeOperationPatterns[pattern] = new OperationPattern { Pattern = pattern, PatternType = patternType, Description = description };
        return this;
    }

    /// <summary>
    /// 添加读操作模式
    /// </summary>
    public PermissionConfigBuilder AddReadOperation(string pattern, PatternType patternType, string description = "")
    {
        _readOperationPatterns[pattern] = new OperationPattern { Pattern = pattern, PatternType = patternType, Description = description };
        return this;
    }

    /// <summary>
    /// 添加 Shell 操作模式
    /// </summary>
    public PermissionConfigBuilder AddShellOperation(string pattern, PatternType patternType, string description = "")
    {
        _shellOperationPatterns[pattern] = new OperationPattern { Pattern = pattern, PatternType = patternType, Description = description };
        return this;
    }

    /// <summary>
    /// 添加敏感路径模式
    /// </summary>
    public PermissionConfigBuilder AddSensitivePath(string path, PathType pathType, string description = "")
    {
        _sensitivePathPatterns[path] = new SensitivePathPattern { Path = path, PathType = pathType, Description = description };
        return this;
    }

    /// <summary>
    /// 添加危险命令模式
    /// </summary>
    public PermissionConfigBuilder AddDangerousCommand(string pattern, string description = "")
    {
        _dangerousCommandPatterns[pattern] = new DangerousCommandPattern { Pattern = pattern, Description = description };
        return this;
    }

    /// <summary>
    /// 使用严格模式（增加更多危险模式）
    /// </summary>
    public PermissionConfigBuilder UseStrictMode()
    {
        _dangerousOperationPatterns["exec"] = new OperationPattern { Pattern = "exec", PatternType = PatternType.Contains, Description = "执行操作" };
        _dangerousOperationPatterns["eval"] = new OperationPattern { Pattern = "eval", PatternType = PatternType.Contains, Description = "求值操作" };
        _sensitivePathPatterns["password"] = new SensitivePathPattern { Path = "password", PathType = PathType.Contains, Description = "密码文件" };
        _sensitivePathPatterns["secret"] = new SensitivePathPattern { Path = "secret", PathType = PathType.Contains, Description = "密钥文件" };
        return this;
    }

    /// <summary>
    /// 使用宽松模式（减少一些限制）
    /// </summary>
    public PermissionConfigBuilder UsePermissiveMode()
    {
        _dangerousOperationPatterns.Remove(OperationTypeConstants.Bash);
        _dangerousOperationPatterns.Remove(OperationTypeConstants.Shell);
        _shellOperationPatterns.Clear();
        return this;
    }

    /// <summary>
    /// 清除所有自动批准的工具
    /// </summary>
    public PermissionConfigBuilder ClearAutoApprovedTools()
    {
        _autoApprovedTools.Clear();
        return this;
    }

    /// <summary>
    /// 清除所有危险命令模式
    /// </summary>
    public PermissionConfigBuilder ClearDangerousCommands()
    {
        _dangerousCommandPatterns.Clear();
        return this;
    }

    /// <summary>
    /// 构建权限配置
    /// </summary>
    public PermissionConfig Build()
    {
        return new PermissionConfig
        {
            AutoApprovedTools = new Dictionary<string, ToolPermissionRule>(_autoApprovedTools, StringComparer.OrdinalIgnoreCase),
            AutoRejectedTools = new Dictionary<string, ToolPermissionRule>(_autoRejectedTools, StringComparer.OrdinalIgnoreCase),
            DangerousOperationPatterns = [.. _dangerousOperationPatterns.Values],
            WriteOperationPatterns = [.. _writeOperationPatterns.Values],
            ReadOperationPatterns = [.. _readOperationPatterns.Values],
            ShellOperationPatterns = [.. _shellOperationPatterns.Values],
            SensitivePathPatterns = [.. _sensitivePathPatterns.Values],
            DangerousCommandPatterns = [.. _dangerousCommandPatterns.Values]
        };
    }
}

#endregion
