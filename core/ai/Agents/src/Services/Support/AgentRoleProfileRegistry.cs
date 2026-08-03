namespace Core.Agents;

using System.Collections.Frozen;
using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.Models.Agent;

/// <summary>
/// Agent 角色注册表 — 管理 AgentRoleProfile 的注册和查询
/// 内置 Profile 在静态构造时注册，用户/项目自定义 Profile 通过 IAgentDefinitionProvider 运行时追加
/// </summary>
[Register(typeof(IAgentRoleRegistry))]
public sealed class AgentRoleProfileRegistry : IAgentRoleRegistry
{
    private readonly IAgentDefinitionProvider? _definitionProvider;
    private readonly ILogger<AgentRoleProfileRegistry>? _logger;
#pragma warning disable JCC4005
    private readonly SemaphoreSlim _loadLock = new(1, 1);
#pragma warning restore JCC4005
    private List<AgentRoleProfile> _profiles;
    private FrozenDictionary<(AgentRole, ExecutorVariant?), AgentRoleProfile> _profileMap;
    private volatile bool _customLoaded;

    public AgentRoleProfileRegistry(
        IAgentDefinitionProvider? definitionProvider = null,
        ILogger<AgentRoleProfileRegistry>? logger = null)
    {
        _definitionProvider = definitionProvider;
        _logger = logger;
        _profiles = BuildBuiltInProfiles();
        _profileMap = BuildProfileMap(_profiles);
    }

    public void Register(AgentRoleProfile profile)
    {
        _loadLock.Wait();
        try
        {
            _profiles.Add(profile);
            _profileMap = BuildProfileMap(_profiles);
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public AgentRoleProfile? GetProfile(AgentRole role, ExecutorVariant? variant = null)
    {
        EnsureCustomLoaded();
        return _profileMap.TryGetValue((role, variant), out var profile) ? profile : null;
    }

    public IEnumerable<AgentRoleProfile> GetAllProfiles()
    {
        EnsureCustomLoaded();
        return _profiles;
    }

    public IEnumerable<AgentRoleProfile> GetProfilesByRole(AgentRole role)
    {
        EnsureCustomLoaded();
        return _profiles.Where(p => p.Role == role);
    }

    public IEnumerable<ExecutorVariant> GetAvailableVariants()
    {
        EnsureCustomLoaded();
        return _profiles
            .Where(p => p.Variant.HasValue)
            .Select(p => p.Variant!.Value)
            .Distinct()
            .OrderBy(v => v);
    }

    public void ClearCache()
    {
        _loadLock.Wait();
        try
        {
            _customLoaded = false;
            _profiles = BuildBuiltInProfiles();
            _profileMap = BuildProfileMap(_profiles);
        }
        finally
        {
            _loadLock.Release();
        }
        _logger?.LogDebug("AgentRoleProfileRegistry 缓存已清除");
    }

    private void EnsureCustomLoaded()
    {
        if (_customLoaded || _definitionProvider is null)
            return;

        _loadLock.Wait();
        try
        {
            if (_customLoaded)
                return;

            try
            {
                var definitions = _definitionProvider.GetAgentDefinitionsAsync().GetAwaiter().GetResult();
                foreach (var def in definitions)
                {
                    var key = (def.Role, def.Variant);
                    if (!_profileMap.ContainsKey(key))
                    {
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
                            OmitClaudeMd = def.OmitClaudeMd,
                            OmitGitStatus = def.OmitGitStatus,
                            IsOneShot = def.Variant.HasValue && OneShotExecutorVariants.IsOneShot(def.Variant.Value),
                            ModelName = def.ModelName,
                            Temperature = def.Temperature,
                            MaxTokens = def.MaxTokens,
                            Memory = def.Memory,
                            Skills = def.Skills,
                            SourcePath = def.SourcePath,
                        };
                        _profiles.Add(profile);
                    }
                }
                _profileMap = BuildProfileMap(_profiles);
                _customLoaded = true;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "加载自定义 AgentDefinition 失败，仅使用内置 Profile");
                _customLoaded = true;
            }
        }
        finally
        {
            _loadLock.Release();
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

    internal static List<AgentRoleProfile> BuildBuiltInProfiles()
    {
        var readOnlyDisallowedTools = new List<string>
        {
            AgentToolNameConstants.Agent, FileToolNameConstants.FileEdit, FileToolNameConstants.FileWrite, NotebookToolNameConstants.NotebookEdit
        };

        var subAgentDisallowedTools = new List<string>
        {
            AgentToolNameConstants.Agent, AgentToolNameConstants.AgentSpawn
        };

        return
        [
            new()
            {
                Role = AgentRole.Coordinator,
                WhenToUse = "General tasks with full toolset",
                Description = "Coordinator agent — manages Goal lifecycle, full toolset",
                AllowedTools = null,
                DisallowedTools = subAgentDisallowedTools,
            },
            new()
            {
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Code,
                WhenToUse = "Code reading, writing, editing and refactoring",
                Description = "Code agent focused on code reading, writing and editing",
                AllowedTools = [FileToolNameConstants.FileRead, FileToolNameConstants.FileWrite, FileToolNameConstants.FileEdit, SearchToolNameConstants.Glob, SearchToolNameConstants.Grep, ShellToolNameConstants.Bash, SearchToolNameConstants.SearchCodebase],
                DisallowedTools = subAgentDisallowedTools,
            },
            new()
            {
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Search,
                WhenToUse = "Code search, navigation and exploration",
                Description = "Search agent focused on code search and navigation",
                AllowedTools = [FileToolNameConstants.FileRead, SearchToolNameConstants.Glob, SearchToolNameConstants.Grep, SearchToolNameConstants.SearchCodebase],
                DisallowedTools = [FileToolNameConstants.FileWrite, FileToolNameConstants.FileEdit, ShellToolNameConstants.Bash],
            },
            new()
            {
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Explore,
                WhenToUse = "Quick codebase exploration agent for file pattern search, keyword search, and codebase Q&A. Supports thoroughness levels: quick/medium/very thorough",
                Description = "Explore agent — strictly read-only, for searching and understanding code",
                AllowedTools = [FileToolNameConstants.FileRead, SearchToolNameConstants.Glob, SearchToolNameConstants.Grep, SearchToolNameConstants.SearchCodebase, ShellToolNameConstants.Bash],
                DisallowedTools = readOnlyDisallowedTools,
                OmitClaudeMd = true,
                OmitGitStatus = true,
                IsOneShot = true,
            },
            new()
            {
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Plan,
                WhenToUse = "Software architect agent that designs implementation plans, returns step-by-step plans, key files, and architectural trade-offs",
                Description = "Plan agent — strictly read-only, for designing implementation plans",
                AllowedTools = [FileToolNameConstants.FileRead, SearchToolNameConstants.Glob, SearchToolNameConstants.Grep, SearchToolNameConstants.SearchCodebase, ShellToolNameConstants.Bash],
                DisallowedTools = readOnlyDisallowedTools,
                OmitClaudeMd = true,
                OmitGitStatus = true,
                IsOneShot = true,
            },
            new()
            {
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Doctor,
                WhenToUse = "自举复盘与修复 — 分析链路日志，发现缺陷，生成修复 patch",
                Description = "Doctor agent — 自举修复，后台运行，Cron 调度每12h复盘",
                AllowedTools = [FileToolNameConstants.FileRead, FileToolNameConstants.FileEdit, SearchToolNameConstants.Glob, SearchToolNameConstants.Grep, ShellToolNameConstants.Bash],
                DisallowedTools = [AgentToolNameConstants.Agent],
                IsBackground = true,
                PermissionMode = "doctor",
            },
            new()
            {
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Verification,
                WhenToUse = "Verify code correctness, quality and security",
                Description = "Verification agent — checks code for errors, vulnerabilities and best practice violations",
                AllowedTools = [FileToolNameConstants.FileRead, SearchToolNameConstants.Glob, SearchToolNameConstants.Grep, SearchToolNameConstants.SearchCodebase, ShellToolNameConstants.Bash],
                DisallowedTools = [AgentToolNameConstants.Agent, FileToolNameConstants.FileEdit, FileToolNameConstants.FileWrite],
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
                Variant = ExecutorVariant.ClaudeCodeGuide,
                WhenToUse = "Guide users on how to use Claude Code features and best practices",
                Description = "Claude Code Guide agent — helps users understand and use Claude Code",
                AllowedTools = [FileToolNameConstants.FileRead, SearchToolNameConstants.Glob, SearchToolNameConstants.Grep, SearchToolNameConstants.SearchCodebase],
                DisallowedTools = [AgentToolNameConstants.Agent, FileToolNameConstants.FileEdit, FileToolNameConstants.FileWrite, ShellToolNameConstants.Bash],
                SystemPrompt = @"你是 Claude Code 使用引导助手。你的任务是帮助用户更好地使用 Claude Code 工具。

## 核心职责
1. 介绍 Claude Code 的功能和特性
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
                AllowedTools = [FileToolNameConstants.FileRead, SearchToolNameConstants.Glob, SearchToolNameConstants.Grep],
                DisallowedTools = [AgentToolNameConstants.Agent, FileToolNameConstants.FileEdit, FileToolNameConstants.FileWrite, ShellToolNameConstants.Bash],
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
