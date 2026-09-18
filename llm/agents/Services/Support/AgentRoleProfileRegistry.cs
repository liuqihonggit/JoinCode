namespace Core.Agents;


/// <summary>
/// Agent 角色注册表 — 管理 AgentRoleProfile 的注册和查询
/// 内置 Profile 在静态构造时注册，用户/项目自定义 Profile 通过 IAgentDefinitionProvider 运行时追加
/// </summary>
[Register(typeof(IAgentRoleRegistry), ServiceLifetime.Singleton)]
public sealed class AgentRoleProfileRegistry : ServiceEntity, IAgentRoleRegistry
{
    private readonly IAgentDefinitionProvider? _definitionProvider;
    private readonly ILogger<AgentRoleProfileRegistry>? _logger;
#pragma warning disable JCC4005
    private readonly AsyncLock _loadLock = new();
#pragma warning restore JCC4005
    private List<AgentRoleProfile> _profiles;
    private FrozenDictionary<(AgentRole, ExecutorVariant?), AgentRoleProfile> _profileMap;
    private Dictionary<AgentRole, List<AgentRoleProfile>> _roleIndex;
    private volatile bool _customLoaded;

    /// <summary>
    /// 构造 AgentRoleProfileRegistry 实例，注入可选的定义提供者与日志器
    /// </summary>
    public AgentRoleProfileRegistry(
        IAgentDefinitionProvider? definitionProvider = null,
        ILogger<AgentRoleProfileRegistry>? logger = null)
    {
        _definitionProvider = definitionProvider;
        _logger = logger;
        _profiles = new List<AgentRoleProfile>();
        _profileMap = BuildProfileMap(_profiles);
        _roleIndex = BuildRoleIndex(_profiles);
    }

    /// <summary>
    /// 注册内置角色 Profile — 由 AgentRolesPlugin 在 InitializeAsync 中调用(ADR 0098 万物皆插件)
    /// </summary>
    public void RegisterBuiltInProfiles()
    {
        foreach (var profile in BuildBuiltInProfiles())
        {
            Register(profile);
        }
    }

    /// <summary>
    /// 撤销内置角色 Profile — 插件卸载时调用
    /// </summary>
    public void UnregisterBuiltInProfiles()
    {
        using var guard = _loadLock.TryLock() ?? throw new System.TimeoutException($"锁 '{_loadLock.Name}' 等待超时");
        var builtIn = BuildBuiltInProfiles().ToHashSet();
        _profiles.RemoveAll(p => builtIn.Contains(p));
        _profileMap = BuildProfileMap(_profiles);
        _roleIndex = BuildRoleIndex(_profiles);
    }

    /// <summary>
    /// 注册单个角色 Profile，更新索引映射
    /// </summary>
    /// <param name="profile">要注册的角色 Profile</param>
    public void Register(AgentRoleProfile profile)
    {
        using var guard = _loadLock.TryLock() ?? throw new System.TimeoutException($"锁 '{_loadLock.Name}' 等待超时");
        _profiles.Add(profile);
        _profileMap = BuildProfileMap(_profiles);
        if (!_roleIndex.TryGetValue(profile.Role, out var list))
        {
            list = new List<AgentRoleProfile>();
            _roleIndex[profile.Role] = list;
        }
        list.Add(profile);
    }

    /// <summary>
    /// 按角色与变体获取角色 Profile
    /// </summary>
    /// <param name="role">代理角色</param>
    /// <param name="variant">执行变体（可选）</param>
    /// <returns>匹配的角色 Profile；未找到时返回 null</returns>
    public AgentRoleProfile? GetProfile(AgentRole role, ExecutorVariant? variant = null)
    {
        EnsureCustomLoaded();
        return _profileMap.TryGetValue((role, variant), out var profile) ? profile : null;
    }

    /// <summary>
    /// 获取所有已注册的角色 Profile
    /// </summary>
    /// <returns>角色 Profile 集合</returns>
    public IEnumerable<AgentRoleProfile> GetAllProfiles()
    {
        EnsureCustomLoaded();
        return _profiles;
    }

    /// <summary>
    /// 按角色获取其下所有变体的 Profile
    /// </summary>
    /// <param name="role">代理角色</param>
    /// <returns>该角色下的所有 Profile</returns>
    public IEnumerable<AgentRoleProfile> GetProfilesByRole(AgentRole role)
    {
        EnsureCustomLoaded();
        return _roleIndex.GetValueOrDefault(role) ?? [];
    }

    /// <summary>
    /// 获取所有可用的执行变体
    /// </summary>
    /// <returns>去重并排序后的执行变体集合</returns>
    public IEnumerable<ExecutorVariant> GetAvailableVariants()
    {
        EnsureCustomLoaded();
        return _profiles
            .Where(p => p.Variant.HasValue)
            .Select(p => p.Variant!.Value)
            .Distinct()
            .OrderBy(v => v);
    }

    /// <summary>
    /// 清除自定义 Profile 缓存，重置为内置 Profile
    /// </summary>
    public void ClearCache()
    {
        using var guard = _loadLock.TryLock() ?? throw new System.TimeoutException($"锁 '{_loadLock.Name}' 等待超时");
        _customLoaded = false;
        _profiles = BuildBuiltInProfiles();
        _profileMap = BuildProfileMap(_profiles);
        _roleIndex = BuildRoleIndex(_profiles);
        _logger?.LogDebug("AgentRoleProfileRegistry 缓存已清除");
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _loadLock.Dispose();
        base.Dispose();
    }

    private void EnsureCustomLoaded()
    {
        if (_customLoaded || _definitionProvider is null)
            return;

        using var guard = _loadLock.TryLock() ?? throw new System.TimeoutException($"锁 '{_loadLock.Name}' 等待超时");
        if (_customLoaded)
            return;

        try
        {
            var definitions = _definitionProvider.GetAgentDefinitionsAsync().GetAwaiter().GetResult();
            var indexMap = _profiles
                .Select((p, i) => (key: (p.Role, p.Variant), i))
                .ToDictionary(x => x.key, x => x.i);
            foreach (var def in definitions)
            {
                var key = (def.Role, def.Variant);
                var profile = new AgentRoleProfile
                {
                    Role = def.Role,
                    Variant = def.Variant,
                    WhenToUse = def.WhenToUse,
                    Description = def.Description,
                    SystemPrompt = def.SystemPrompt,
                    AllowedTools = def.Tools,
                    DisallowedTools = def.DisallowedTools,
                    PermissionMode = def.PermissionMode,
                    IsBackground = def.IsBackground,
                    OmitProjectRules = def.OmitProjectRules,
                    OmitGitStatus = def.OmitGitStatus,
                    IsOneShot = def.Variant.HasValue && OneShotExecutorVariants.IsOneShot(def.Variant.Value),
                    ModelName = def.ModelName,
                    Temperature = def.Temperature,
                    MaxTokens = def.MaxTokens,
                    Memory = def.Memory,
                    Skills = def.Skills,
                    SourcePath = def.SourcePath,
                    CriticalSystemReminder = def.CriticalSystemReminder,
                };

                if (indexMap.TryGetValue(key, out var existingIdx) && def.SourcePath is not null)
                {
                    _profiles[existingIdx] = profile;
                }
                else if (!indexMap.ContainsKey(key))
                {
                    _profiles.Add(profile);
                    indexMap[key] = _profiles.Count - 1;
                }
            }
            _profileMap = BuildProfileMap(_profiles);
            _roleIndex = BuildRoleIndex(_profiles);
            _customLoaded = true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "加载自定义 AgentDefinition 失败，仅使用内置 Profile");
            _customLoaded = true;
        }
    }

    private static FrozenDictionary<(AgentRole, ExecutorVariant?), AgentRoleProfile> BuildProfileMap(
        List<AgentRoleProfile> profiles)
    {
        var builder = new Dictionary<(AgentRole, ExecutorVariant?), AgentRoleProfile>();
        foreach (var p in profiles)
        {
            builder.TryAdd((p.Role, p.Variant), p);
        }
        return builder.ToFrozenDictionary();
    }

    private static Dictionary<AgentRole, List<AgentRoleProfile>> BuildRoleIndex(List<AgentRoleProfile> profiles)
    {
        var index = new Dictionary<AgentRole, List<AgentRoleProfile>>();
        foreach (var p in profiles)
        {
            if (!index.TryGetValue(p.Role, out var list))
            {
                list = new List<AgentRoleProfile>();
                index[p.Role] = list;
            }
            list.Add(p);
        }
        return index;
    }

    private static bool IsCoordinatorModeEnabledFromEnv()
    {
        var value = Environment.GetEnvironmentVariable("JCC_COORDINATOR_MODE");
        return value is "1" or "true" or "True" or "TRUE";
    }

    /// <summary>
    /// 构建内置角色 Profile 列表 — 包含 Coordinator 及 Executor 各变体（Code/Search/Explore/Plan/Doctor/Verification 等）
    /// </summary>
    /// <returns>内置角色 Profile 列表</returns>
    internal static List<AgentRoleProfile> BuildBuiltInProfiles()
    {
        var readOnlyDisallowedTools = new List<string>
        {
            AgentToolNameEnumConstants.Agent, FileToolNameEnumConstants.FileEdit, FileToolNameEnumConstants.FileWrite, NotebookToolNameEnumConstants.NotebookEdit
        };

        var subAgentDisallowedTools = new List<string>
        {
            AgentToolNameEnumConstants.Agent, AgentToolNameEnumConstants.AgentSpawn
        };

        return
        [
            new()
            {
                Role = AgentRole.Coordinator,
                WhenToUse = "General tasks with full toolset",
                Description = "Coordinator agent — manages Goal lifecycle, full toolset",
                AllowedTools = IsCoordinatorModeEnabledFromEnv()
                    ? [AgentToolNameEnumConstants.Agent, AgentToolNameEnumConstants.AgentSendMessage, TaskToolNameEnumConstants.TaskStop]
                    : [],
                DisallowedTools = subAgentDisallowedTools,
            },
            new()
            {
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Code,
                WhenToUse = "Code reading, writing, editing and refactoring",
                Description = "Code agent focused on code reading, writing and editing",
                AllowedTools = [FileToolNameEnumConstants.FileRead, FileToolNameEnumConstants.FileWrite, FileToolNameEnumConstants.FileEdit, SearchToolNameEnumConstants.Glob, SearchToolNameEnumConstants.Grep, ShellToolNameEnumConstants.Bash, SearchToolNameEnumConstants.SearchCodebase],
                DisallowedTools = subAgentDisallowedTools,
            },
            new()
            {
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Search,
                WhenToUse = "Code search, navigation and exploration",
                Description = "Search agent focused on code search and navigation",
                AllowedTools = [FileToolNameEnumConstants.FileRead, SearchToolNameEnumConstants.Glob, SearchToolNameEnumConstants.Grep, SearchToolNameEnumConstants.SearchCodebase],
                DisallowedTools = [FileToolNameEnumConstants.FileWrite, FileToolNameEnumConstants.FileEdit, ShellToolNameEnumConstants.Bash],
            },
            new()
            {
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Explore,
                WhenToUse = "Quick codebase exploration agent for file pattern search, keyword search, and codebase Q&A. Supports thoroughness levels: quick/medium/very thorough",
                Description = "Explore agent — strictly read-only, for searching and understanding code",
                AllowedTools = [FileToolNameEnumConstants.FileRead, SearchToolNameEnumConstants.Glob, SearchToolNameEnumConstants.Grep, SearchToolNameEnumConstants.SearchCodebase, ShellToolNameEnumConstants.Bash],
                DisallowedTools = readOnlyDisallowedTools,
                OmitProjectRules = true,
                OmitGitStatus = true,
                IsOneShot = true,
            },
            new()
            {
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Plan,
                WhenToUse = "Software architect agent that designs implementation plans, returns step-by-step plans, key files, and architectural trade-offs",
                Description = "Plan agent — strictly read-only, for designing implementation plans",
                AllowedTools = [FileToolNameEnumConstants.FileRead, SearchToolNameEnumConstants.Glob, SearchToolNameEnumConstants.Grep, SearchToolNameEnumConstants.SearchCodebase, ShellToolNameEnumConstants.Bash],
                DisallowedTools = readOnlyDisallowedTools,
                OmitProjectRules = true,
                OmitGitStatus = true,
                IsOneShot = true,
            },
            new()
            {
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Doctor,
                WhenToUse = "自举复盘与修复 — 分析链路日志，发现缺陷，生成修复 patch",
                Description = "Doctor agent — 自举修复，后台运行，Cron 调度每12h复盘",
                AllowedTools = [FileToolNameEnumConstants.FileRead, FileToolNameEnumConstants.FileEdit, SearchToolNameEnumConstants.Glob, SearchToolNameEnumConstants.Grep, ShellToolNameEnumConstants.Bash],
                DisallowedTools = [AgentToolNameEnumConstants.Agent],
                IsBackground = true,
                PermissionMode = "doctor",
            },
            new()
            {
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Verification,
                WhenToUse = "Verify code correctness, quality and security",
                Description = "Verification agent — checks code for errors, vulnerabilities and best practice violations",
                AllowedTools = [FileToolNameEnumConstants.FileRead, SearchToolNameEnumConstants.Glob, SearchToolNameEnumConstants.Grep, SearchToolNameEnumConstants.SearchCodebase, ShellToolNameEnumConstants.Bash],
                DisallowedTools = [AgentToolNameEnumConstants.Agent, FileToolNameEnumConstants.FileEdit, FileToolNameEnumConstants.FileWrite],
                SystemPrompt = @"你是一个代码验证助手。你的任务是验证代码的正确性、质量和安全性。

## 核心职责
1. 检查代码语法和逻辑错误
2. 验证代码是否符合最佳实践
3. 识别潜在的安全漏洞
4. 评估代码质量和可维护性

## 验证维度
- 语法正确性
- 逻辑完整性
- 代码风格一致性
- 异常处理
- 性能考虑
- 安全性检查

## 输出格式
1. 验证概述
2. 发现的问题（按严重程度分类）
3. 改进建议
4. 最佳实践参考

请使用中文回复，保持客观且建设性的态度。",
            },
            new()
            {
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.JoinCodeGuide,
                WhenToUse = $"Guide users on how to use {BrandConstants.ProductName} features and best practices",
                Description = $"{BrandConstants.ProductName} Guide agent — helps users understand and use {BrandConstants.ProductName}",
                AllowedTools = [FileToolNameEnumConstants.FileRead, SearchToolNameEnumConstants.Glob, SearchToolNameEnumConstants.Grep, SearchToolNameEnumConstants.SearchCodebase],
                DisallowedTools = [AgentToolNameEnumConstants.Agent, FileToolNameEnumConstants.FileEdit, FileToolNameEnumConstants.FileWrite, ShellToolNameEnumConstants.Bash],
                SystemPrompt = $@"你是 {BrandConstants.ProductName} 使用引导助手。你的任务是帮助用户更好地使用 {BrandConstants.ProductName} 工具。

## 核心职责
1. 介绍 {BrandConstants.ProductName} 的功能和特性
2. 指导用户如何有效使用各种工具
3. 解答使用过程中的疑问
4. 提供最佳实践和技巧

## 功能介绍
- Agent 模式：自动规划和执行任务
- Plan 模式：制定详细执行计划
- Spec 模式：编写规范文档
- 各种工具的使用方法

## 引导原则
- 根据用户水平调整解释深度
- 提供具体的示例和用法
- 解释背后的设计思想
- 帮助用户建立正确的工作流程

请使用中文回复，保持耐心且易于理解的表达。",
            },
            new()
            {
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.ContextCompression,
                WhenToUse = "Intelligently compress and manage conversation context to optimize Token usage",
                Description = "Context Compression agent — compresses context while preserving key information",
                AllowedTools = [FileToolNameEnumConstants.FileRead, SearchToolNameEnumConstants.Glob, SearchToolNameEnumConstants.Grep],
                DisallowedTools = [AgentToolNameEnumConstants.Agent, FileToolNameEnumConstants.FileEdit, FileToolNameEnumConstants.FileWrite, ShellToolNameEnumConstants.Bash],
                SystemPrompt = @"你是上下文压缩助手。你的任务是智能地压缩和管理对话上下文，以优化 Token 使用并保留关键信息。

## 核心职责
1. 分析当前上下文的 Token 使用情况
2. 识别可以安全压缩的内容区域
3. 生成高质量的摘要替代详细内容
4. 保留关键决策点、重要信息和上下文连续性

## 压缩策略
- 分层压缩：Detailed → Summary → Index
- 代码内容：保留签名和关键逻辑，压缩实现细节
- 对话历史：保留关键决策点，压缩闲聊内容
- 日志内容：保留错误和警告，压缩常规信息

## 保留优先级（从高到低）
1. 用户明确标记为重要的内容
2. 关键决策点和结论
3. 函数/方法签名和接口定义
4. 错误信息和异常处理
5. 最近的对话轮次

请使用中文回复，保持专业且系统化的表达方式。",
            }
        ];
    }
}
