
namespace Core.Configuration;

/// <summary>
/// 权限检查配置
/// </summary>
public class PermissionConfig
{
    /// <summary>
    /// 自动批准的工具列表
    /// </summary>
    public List<ToolPermissionRule> AutoApprovedTools { get; set; } = new();

    /// <summary>
    /// 自动拒绝的工具列表
    /// </summary>
    public List<ToolPermissionRule> AutoRejectedTools { get; set; } = new();

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
    /// 创建默认配置
    /// </summary>
    public static PermissionConfig CreateDefault()
    {
        return new PermissionConfig
        {
            AutoApprovedTools =
            [
                new ToolPermissionRule { ToolName = FileToolNameConstants.FileRead, Description = "Read file" },
                new ToolPermissionRule { ToolName = "file_list", Description = "List files" },
                new ToolPermissionRule { ToolName = FileToolNameConstants.DirectoryList, Description = "List directory" },
                new ToolPermissionRule { ToolName = SearchToolNameConstants.Glob, Description = "File pattern matching" },
                new ToolPermissionRule { ToolName = SearchToolNameConstants.Grep, Description = "Text search" },
                // WebFetch 不在 AutoApprovedTools 中 — 对齐 TS 版: WebFetch 需要域名级权限检查
                // 预批准域名由 PreapprovedDomains 管理，用户可通过 /allowed-tools 添加域名白名单
                // WebSearch 只读操作，自动批准
                new ToolPermissionRule { ToolName = WebToolNameConstants.WebSearch, Description = "Web search" },
                new ToolPermissionRule { ToolName = TaskToolNameConstants.TaskList, Description = "List tasks" },
                new ToolPermissionRule { ToolName = TaskToolNameConstants.TaskGet, Description = "Get task" },
                new ToolPermissionRule { ToolName = SystemToolNameConstants.TaskOutput, Description = "Get task output" }
            ],
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
public class ToolPermissionRule
{
    [Required]
    public string ToolName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

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
public class OperationPattern
{
    [Required]
    public string Pattern { get; set; } = string.Empty;

    public PatternType PatternType { get; set; } = PatternType.Contains;

    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// 敏感路径模式
/// </summary>
public class SensitivePathPattern
{
    [Required]
    public string Path { get; set; } = string.Empty;

    public PathType PathType { get; set; } = PathType.Contains;

    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// 危险命令模式
/// </summary>
public class DangerousCommandPattern
{
    [Required]
    public string Pattern { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
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
    private readonly List<ToolPermissionRule> _autoApprovedTools = new();
    private readonly List<ToolPermissionRule> _autoRejectedTools = new();
    private readonly List<OperationPattern> _dangerousOperationPatterns = new();
    private readonly List<OperationPattern> _writeOperationPatterns = new();
    private readonly List<OperationPattern> _readOperationPatterns = new();
    private readonly List<OperationPattern> _shellOperationPatterns = new();
    private readonly List<SensitivePathPattern> _sensitivePathPatterns = new();
    private readonly List<DangerousCommandPattern> _dangerousCommandPatterns = new();

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
        
        builder._autoApprovedTools.AddRange(defaultConfig.AutoApprovedTools);
        builder._dangerousOperationPatterns.AddRange(defaultConfig.DangerousOperationPatterns);
        builder._writeOperationPatterns.AddRange(defaultConfig.WriteOperationPatterns);
        builder._readOperationPatterns.AddRange(defaultConfig.ReadOperationPatterns);
        builder._shellOperationPatterns.AddRange(defaultConfig.ShellOperationPatterns);
        builder._sensitivePathPatterns.AddRange(defaultConfig.SensitivePathPatterns);
        builder._dangerousCommandPatterns.AddRange(defaultConfig.DangerousCommandPatterns);
        
        return builder;
    }

    /// <summary>
    /// 添加自动批准的工具
    /// </summary>
    public PermissionConfigBuilder AddAutoApprovedTool(string toolName, string description = "")
    {
        _autoApprovedTools.Add(new ToolPermissionRule { ToolName = toolName, Description = description });
        return this;
    }

    /// <summary>
    /// 添加自动拒绝的工具
    /// </summary>
    public PermissionConfigBuilder AddAutoRejectedTool(string toolName, string description = "")
    {
        _autoRejectedTools.Add(new ToolPermissionRule { ToolName = toolName, Description = description });
        return this;
    }

    /// <summary>
    /// 添加危险操作模式
    /// </summary>
    public PermissionConfigBuilder AddDangerousOperation(string pattern, PatternType patternType, string description = "")
    {
        _dangerousOperationPatterns.Add(new OperationPattern { Pattern = pattern, PatternType = patternType, Description = description });
        return this;
    }

    /// <summary>
    /// 添加写操作模式
    /// </summary>
    public PermissionConfigBuilder AddWriteOperation(string pattern, PatternType patternType, string description = "")
    {
        _writeOperationPatterns.Add(new OperationPattern { Pattern = pattern, PatternType = patternType, Description = description });
        return this;
    }

    /// <summary>
    /// 添加读操作模式
    /// </summary>
    public PermissionConfigBuilder AddReadOperation(string pattern, PatternType patternType, string description = "")
    {
        _readOperationPatterns.Add(new OperationPattern { Pattern = pattern, PatternType = patternType, Description = description });
        return this;
    }

    /// <summary>
    /// 添加 Shell 操作模式
    /// </summary>
    public PermissionConfigBuilder AddShellOperation(string pattern, PatternType patternType, string description = "")
    {
        _shellOperationPatterns.Add(new OperationPattern { Pattern = pattern, PatternType = patternType, Description = description });
        return this;
    }

    /// <summary>
    /// 添加敏感路径模式
    /// </summary>
    public PermissionConfigBuilder AddSensitivePath(string path, PathType pathType, string description = "")
    {
        _sensitivePathPatterns.Add(new SensitivePathPattern { Path = path, PathType = pathType, Description = description });
        return this;
    }

    /// <summary>
    /// 添加危险命令模式
    /// </summary>
    public PermissionConfigBuilder AddDangerousCommand(string pattern, string description = "")
    {
        _dangerousCommandPatterns.Add(new DangerousCommandPattern { Pattern = pattern, Description = description });
        return this;
    }

    /// <summary>
    /// 使用严格模式（增加更多危险模式）
    /// </summary>
    public PermissionConfigBuilder UseStrictMode()
    {
        _dangerousOperationPatterns.Add(new OperationPattern { Pattern = "exec", PatternType = PatternType.Contains, Description = "执行操作" });
        _dangerousOperationPatterns.Add(new OperationPattern { Pattern = "eval", PatternType = PatternType.Contains, Description = "求值操作" });
        _sensitivePathPatterns.Add(new SensitivePathPattern { Path = "password", PathType = PathType.Contains, Description = "密码文件" });
        _sensitivePathPatterns.Add(new SensitivePathPattern { Path = "secret", PathType = PathType.Contains, Description = "密钥文件" });
        return this;
    }

    /// <summary>
    /// 使用宽松模式（减少一些限制）
    /// </summary>
    public PermissionConfigBuilder UsePermissiveMode()
    {
        _dangerousOperationPatterns.RemoveAll(p => p.Pattern == OperationTypeConstants.Bash || p.Pattern == OperationTypeConstants.Shell);
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
            AutoApprovedTools = new List<ToolPermissionRule>(_autoApprovedTools),
            AutoRejectedTools = new List<ToolPermissionRule>(_autoRejectedTools),
            DangerousOperationPatterns = new List<OperationPattern>(_dangerousOperationPatterns),
            WriteOperationPatterns = new List<OperationPattern>(_writeOperationPatterns),
            ReadOperationPatterns = new List<OperationPattern>(_readOperationPatterns),
            ShellOperationPatterns = new List<OperationPattern>(_shellOperationPatterns),
            SensitivePathPatterns = new List<SensitivePathPattern>(_sensitivePathPatterns),
            DangerousCommandPatterns = new List<DangerousCommandPattern>(_dangerousCommandPatterns)
        };
    }
}

#endregion
