# 项目架构索引

> 此文档从 README 摘出，详细描述项目目录结构、组件依赖图和内部结构。

> 📖 **架构决策记录（ADR）**：重要的"为什么这样选"决策收编在 [docs/adr/](../adr/README.md)，共 40+ 条。AGENTS.md 各规则处标注了对应 ADR 编号，可二次打开查看完整决策上下文与替代方案。

## 1. 顶层目录

```
JoinCode/
├── generators/          ★ 9 个源码生成器（netstandard2.0）
├── foundation/          ★ 基础抽象（Abstractions + Structura + Transport.Contracts）
├── infrastructure/      ★ 基础设施（Infrastructure + Transport.Impl）
├── core/                ★ 核心组件（ai/execution/safety/search 四个子域）
├── services/            ★ 服务组件（Mcp + Dream + Eyes + Bridge + SandboxSatellite）
├── composition/         ★ 组合层（Composition + Clock）
├── app/                 ★ 主工程（JoinCode.exe + Sdk）
├── tests/               ★ 单元/集成/MockServer/基准/AOT兼容测试
├── tools/               ★ 辅助工具（AST审计）
├── docs/                ★ 文档
├── build.ps1            主构建脚本
├── Generators.slnx      ① 生成器解决方案
├── Foundation.slnx      ② 基础抽象解决方案
├── Infrastructure.slnx  ③ 基础设施解决方案
├── Core.slnx            ④ 核心组件解决方案
├── Services.slnx        ⑤ 服务组件解决方案
├── Composition.slnx     ⑥ 组合层解决方案
└── App.slnx             ⑦ 主工程解决方案（Host + tests + MockServers）
```

> **💡 `JoinCode.slnx`（全量聚合解决方案）**
>
> 根目录还有 `JoinCode.slnx`，聚合了上述七层的**全部 89 个项目**（含 src + tests + tools + MockServers + Benchmarks + AotCompatibility）。
>
> | 用途 | 说明 |
> |------|------|
> | **VS / Rider 一站式浏览** | 用 Visual Studio 或 JetBrains Rider 打开此文件，可在同一解决方案树中查看所有组件，无需分别加载 7 个 slnx |
> | **全量重构/查找引用** | 跨组件全局重命名、查找引用、调用链分析时，一个窗口覆盖全部代码 |
> | **不参与 CI** | CI 仍按七层顺序编译各自的 slnx（`Generators → Foundation → ... → App`），`JoinCode.slnx` 仅供 IDE 浏览，不用于构建/测试流水线 |
>
> **何时用哪个？**
> - 日常开发改单个组件 → 用对应层级的 slnx（如改 Llm 用 `Core.slnx`）
> - 需要全局鸟瞰/跨层重构 → 用 `JoinCode.slnx`

## 2. 基础层（所有组件的公共依赖）

| 项目 | 路径 | 职责 | 关键 NuGet |
|------|------|------|-----------|
| **Abstractions** | `foundation/Abstractions/` | 纯接口 + DTO + 管道契约 + 特性标记（零实现） | Microsoft.Extensions.DI |
| **Infrastructure** | `infrastructure/Infrastructure/` | 管道核心/缓存/IO/遥测/本地化/SSH/插件 | YamlDotNet, Microsoft.Extensions.Hosting |

> **Abstractions** 内部按层分区：`00-core/`（Attributes, Configuration, Models, Pipeline, State...）、`01-ai/`（LLM, Mcp, Prompts）、`02-brain/`（Chat, Context, Query）、`03-hands/`（Shell, Skill, Tools）、`04-guard/`（Security）、`05-memory/`（Conversation, FileIO）、`06-perception/`（CodeIndex, Lsp, Web）、`07-agents/`（Agent, Team）、`08-transport/`（Bridge, Build）、`09-composition/`（Mode, Presentation）

## 3. 组件依赖图（无环分层）

```
L0 叶子（零组件间依赖）:
  Structura            → （零外部依赖）
  Transport.Contracts  → Abstractions
  Transport.Impl       → Transport.Contracts, Abstractions
  Llm                  → Abstractions, Infrastructure, Transport.Contracts
  CodeIndex            → Abstractions, Infrastructure
  Browser              → Abstractions, Infrastructure

L1:
  Bridge               → Abstractions, Infrastructure, Transport.Contracts, Transport.Impl
  Mcp                  → Abstractions, Infrastructure, Llm, Transport.Contracts, Transport.Impl
  Dream                → Abstractions, Infrastructure, Llm

L2:
  Guard                → Abstractions, Infrastructure
  Eyes                 → Abstractions, CodeIndex
  Vault                → Abstractions, Infrastructure

L3:
  Brain                → Abstractions, Infrastructure

L4:
  Hands                → Abstractions, Infrastructure

L5:
  McpToolDispatch      → Abstractions, Infrastructure
  Scheduling           → Abstractions, Infrastructure, Structura
  Agents               → Abstractions, Infrastructure
  Reasoning            → Abstractions, Infrastructure, Structura, Agents

L6 组合根:
  Composition          → Bridge, Mcp, Brain, Guard, Hands, Eyes, Vault, Scheduling, McpToolDispatch, Agents, Reasoning, Transport.Contracts, Transport.Impl

L7:
  Clock                → Composition, Vault, Scheduling

Host:
  JoinCode (jcc.exe)   → Brain, Hands, Eyes, Vault, Composition, Guard, Clock, Bridge, Dream, Browser, Transport.Contracts, Transport.Impl
```

> 所有组件隐式依赖 `Abstractions` + `Infrastructure`（上表省略以突出组件间关系）

## 4. 组件详情

| 组件 | 路径 | 层 | 职责 | 关键 NuGet | 源码生成器 |
|------|------|----|------|-----------|-----------|
| Transport.Contracts | `foundation/Transport.Contracts/` | L0 | 传输协议契约 | — | Enum, CI |
| Transport.Impl | `infrastructure/Transport.Impl/` | L0 | 传输实现 | — | CI |
| Structura | `foundation/Structura/` | L0 | 通用DAG数据结构（拓扑排序/环检测/增量重算/线程安全） | — | — |
| Llm | `core/ai/Llm/` | L0 | LLM 适配器（OpenAI/Anthropic/Azure/Pipe） | Microsoft.Extensions.DI, Options | Enum, CI |
| CodeIndex | `core/search/CodeIndex/` | L0 | 代码索引引擎（TreeSitter） | TreeSitter.DotNet | CI |
| Browser | `core/search/Browser/` | L0 | 浏览器自动化（卫星包） | PuppeteerSharp | CI |
| Bridge | `services/Bridge/` | L1 | 进程桥接服务 | Microsoft.Extensions.Hosting, QRCoder | Enum, CI, CliOption |
| Mcp | `services/Mcp/` | L1 | MCP 协议客户端 | ModelContextProtocol, Microsoft.Extensions.DI | Enum, CI |
| Dream | `services/Dream/` | L1 | 记忆整合插件 | Microsoft.Extensions.Hosting | Enum, CI, CliOption, McpTool |
| Guard | `core/safety/Guard/` | L2 | 权限/安全/Hook/OAuth | Microsoft.Extensions.Http, TreeSitter.DotNet | Enum, CI |
| Eyes | `services/Eyes/` | L2 | 代码索引服务/LSP | Microsoft.Extensions.Hosting | Enum, CI |
| Vault | `core/safety/Vault/` | L2 | 记忆目录/状态/待办/通知 | Microsoft.Data.Sqlite, SQLitePCLRaw | CI |
| Brain | `core/execution/Brain/` | L3 | 查询引擎/上下文/提示词/计划/成本 | Microsoft.Extensions.Options | Enum, CI, PromptSection |
| Hands | `core/execution/Hands/` | L4 | 工具执行/Shell/Web/Notebook/API/缓存 | ImageSharp, Docnet.Core, ReverseMarkdown.Aot | CI |
| McpToolDispatch | `core/execution/McpToolDispatch/` | L5 | MCP 工具处理器 | ModelContextProtocol | McpTool, Enum, CI |
| Scheduling | `core/execution/Scheduling/` | L5 | 任务调度/Cron/持久化 | Microsoft.Extensions.DI | Enum, CI |
| Agents | `core/ai/Agents/` | L5 | Agent 协调/生命周期/Fork/Team | Microsoft.Extensions.Caching.Memory | McpTool, Enum, CI |
| Reasoning | `core/ai/Reasoning/` | L5 | 结构化推理/三权分立/双预算 | Microsoft.Extensions.Logging | Enum, CI |
| SandboxSatellite | `services/SandboxSatellite/` | L1 | 沙箱卫星进程 | — | CI |
| Composition | `composition/Composition/` | L6 | 依赖注入集成层（组合根） | ModelContextProtocol | Enum, CI, McpTool |
| Clock | `composition/Clock/` | L7 | 目标引擎/工作流宿主 | Microsoft.Extensions.Logging | CI |

> **Enum** = EnumMetadata.Generator, **CI** = McpToolDispatch.Generator（[Register] DI 注册）, **McpTool** = McpToolDispatch.Generator, **PromptSection** = PromptSection.Generator, **CliOption** = CliOption.Generator

## 5. 组件内部结构

### Brain (`02-brain/Brain/src/`)
```
Cache/          上下文缓存
Context/        上下文管理
ContextFold/    上下文折叠/压缩
CostTracking/   成本追踪
Planning/       计划模式
Prompts/        提示词构建
Query/          查询引擎
Summary/        摘要
```

### Hands (`03-hands/Hands/src/`)
```
Api/            API 调用
Build/          构建拦截
Cache/          缓存服务
FileOps/        文件操作
Integration/    集成服务
Network/        网络服务
Notebook/       Notebook 支持
Shell/          Shell 执行
Skills/         技能系统
System/         系统服务
ToolHandlers/   工具处理器
Voice/          语音服务
Web/            Web 请求
```

### Guard (`04-guard/Guard/src/`)
```
Configuration/  配置加载
Hooks/          Hook 系统
OAuth/          OAuth 认证
Permission/     权限管理
Policy/         策略引擎
Security/       安全护栏
```

### Vault (`05-memory/Vault/src/`)
```
Memdir/         记忆目录
Notification/   通知
State/          状态持久化
StepEvidence/   步骤证据
Todo/           待办事项
UserInteraction/ 用户交互
```

### Mcp (`01-ai/Mcp/src/`)
```
Auth/           认证
Client/         客户端
Communication/  通信
Core/           核心
Dev/            开发工具
McpProtocol/    MCP 协议
Models/         数据模型
Protocol.Contracts/ 协议契约
Remote/         远程客户端
Skill/          技能
Task/           任务
Terminal/       终端
Transports/     传输层
User/           用户
Utils/          工具
Workflow/       工作流
```

### Llm (`01-ai/Llm/src/`)
```
Adapters/       LLM 适配器（OpenAI/Anthropic/Azure/Pipe）
Registration/   注册服务
```

### Reasoning (`10-reasoning/Reasoning/src/`)
```
Agents/         推理Agent基类(ReasoningAgent)+三权实现(控方/辩方/法官)
Engine/         推理引擎+配置+预算状态+摘要
Evidence/       数据项+证据+裁决
State/          枚举（状态/信任度/预设/续费方式）
DependencyInjection/ DI注册
```

## 6. 源码生成器

| 生成器 | 路径 | 用途 | 使用范围 |
|--------|------|------|---------|
| AotSafety.Generator | `generators/AotSafety.Generator/` | AOT 安全分析器 + 代码组织规则（JCC9006 强制 `FileShare.ReadWrite`、JCC5002 禁止循环内 `+=` 拼字符串、层依赖审计、抽象层绕过检测） | 全局（根 Directory.Build.props） |
| CodeFixes | `generators/CodeFixes/` | JCC 代码修复 | 全局（根 Directory.Build.props） |
| EnumMetadata.Generator | `generators/EnumMetadata.Generator/` | 枚举元数据（[EnumValue] → XxxConstants + XxxExtensions）+ SettingsMerge | 几乎所有组件 |
| McpToolDispatch.Generator | `generators/McpToolDispatch.Generator/` | MCP 工具处理器注册 + [Register] DI 注册 + Command 注册 | McpToolDispatch, Agents, Composition, Dream, JoinCode 及所有用 [Register] 的组件 |
| PromptSection.Generator | `generators/PromptSection.Generator/` | 提示词段落生成 | Brain |
| PromptTemplate.Generator | `generators/PromptTemplate.Generator/` | 提示词模板生成 | Brain |
| ToolPrompt.Generator | `generators/ToolPrompt.Generator/` | 工具提示生成 | Hands |
| CliOption.Generator | `generators/CliOption.Generator/` | CLI 选项绑定 | Bridge, Dream, JoinCode |
| AppModule.Generator | `generators/AppModule.Generator/` | 应用模块注册 | JoinCode |

**分析器铁律**：

| 规则 | 说明 | 触发场景 |
|------|------|----------|
| `JCC5002` | 循环内禁止 `+=` 拼字符串，流式追加用 `StringBuilder` | 性能热点循环 |
| `JCC9006` | `FileStream` 构造必须用 `FileShare.ReadWrite`（避免跨进程读写冲突） | 所有 `new FileStream(...)` 调用，`PhysicalFileSystem`/`SafeFileIO` 已豁免 |

## 7. Host 项目 (`app/JoinCode/`)

```
Adapters/       适配器
App/            应用初始化
Cli/            CLI 解析
Commands/       命令处理
Entry/          入口点
Pipe/           管道
Services/       服务
Program.cs      主入口
```

**Host 引用**：Brain, Hands, Eyes, Vault, Composition, Guard, Clock, Bridge, Dream, Browser, Transport.Contracts, Transport.Impl + 4 个 Analyzer（McpTool, Enum, CliOption, AppModule）

**关键 NuGet**：Microsoft.Extensions.Hosting, System.CommandLine

## 8. 测试结构

```
tests/
├── AotCompatibility/            AOT 兼容性测试
├── Unit/
│   ├── Abs.Tests/               Abstractions 单元测试
│   ├── Hands.Tests/             Hands 单元测试
│   ├── Host.Tests/              Host 单元测试
│   ├── Infra.Tests/             Infrastructure 单元测试
│   ├── Mcp.Tests/               Mcp 单元测试
│   ├── McpToolDispatch.Tests/   McpToolDispatch 单元测试
│   └── Testing.Common/          测试公共库
├── Integration/
│   └── Integration.Tests/       集成测试
├── MockServers/
│   ├── MockServer.Core/         Mock 核心库
│   ├── OpenAI.MockServer/       OpenAI 模拟服务
│   ├── Anthropic.MockServer/    Anthropic 模拟服务
│   ├── DeepSeek.MockServer/     DeepSeek 模拟服务
│   ├── Mcp.MockServer/          MCP 模拟服务
│   ├── MockServer.Core.Tests/   Mock 核心测试
│   ├── MockServer.E2E.Tests/    E2E 测试
│   ├── Sync.Integration.Tests/  同步集成测试
│   └── scripts/                 测试脚本
└── Benchmarks/
    └── Eyes.Benchmarks/         性能基准
```

**组件测试**：每个组件有 `tests/` 子目录，如 `services/Mcp/tests/Unit/Mcp.Tests.csproj`

## 9. SDK 聚合包 (`app/Sdk/`)

一行代码引用所有组件：`JoinCode.Sdk` 引用 Abstractions + Infrastructure + 全部组件

## 10. 中间件管道清单

| 管道 | 接口 | 子系统 | 中间件链 |
|------|------|--------|---------|
| Chat | `StreamMiddlewarePipeline<ChatMiddlewareContext, ChatStreamEvent>` | brain | Timing→ErrorHandling→AuditLog→TokenBudget→PreChat→QueryLoop→LoopIntervention→ProcessUsage→CleanupInjections→SaveContext |
| ChatInit | `MiddlewarePipeline<ChatInitContext>` | brain | ContextLoad→CostRestore→ConfigChangeStart→SessionStartHook |
| ChatAdmin | `MiddlewarePipeline<ChatAdminContext>` | brain | SessionAdmin→SessionSave |
| Preprocess | `MiddlewarePipeline<PreprocessContext>` | brain | KeywordInjection→SynonymInjection→SystemPrompt→ReminderInjection→ToolListingInjection→LspDiagnostic |
| Compact | `MiddlewarePipeline<CompactContext>` | brain | CompactHook→ContextCollapse→Microcompact→SessionMemoryCompact→ReactiveCompact |
| Query | `MiddlewarePipeline<QueryMiddlewareContext>` | brain | UsdBudget→QueryTokenBudget→CostTracking→DiminishingReturns→HistorySnip→IdleReminder→StopHook→StateTransition→ContentReplacement |
| Permission | `MiddlewarePipeline<PermissionCheckContext>` | Guard | Bypass→AgentRestriction→AutoClassifier→ConfigGetOperation→WebFetchPermission→EarlyPathDeny→ToolListPermission→PathPermission→DangerousOperation→PlanMode→AutoSafety→DefaultResult |
| Settings | `MiddlewarePipeline<SettingsContext>` | Guard | SettingsReload→EffortLevel→HookRefresh→PermissionCache |
| AgentSpawn | `MiddlewarePipeline<AgentSpawnContext>` | Agents | DefinitionResolution→PromptBuilding→ContextSetup→AgentWorktreeSpawn→HookSetup→McpSetup→Metadata→Transcript |
| Fork | `MiddlewarePipeline<ForkContext>` | Agents | ForkValidation→ForkSpawn→ForkPermission→ForkExecution |
| Web | `MiddlewarePipeline<WebContext>` | Hands | Metrics→Validation→SsrfGuard→CacheCheck→DomainCheck→Fetch→ContentProcessing→CacheWrite |
| Shell | `MiddlewarePipeline<ShellContext>` | Hands | Validation→Classification→SedIntercept→Background→BuildIntercept→Execution→Output |
| Skill | `MiddlewarePipeline<SkillContext>` | Hands | Metrics→Validation→Telemetry→Execution |
| Code | `MiddlewarePipeline<CodeContext>` | Hands | Cache→Security→Llm→Sandbox→Metrics |

## 11. 关键配置文件

| 文件 | 路径 | 说明 |
|------|------|------|
| 根 Directory.Build.props | `Directory.Build.props` | 全局：net10.0, AOT, 版本变量, 全局 Analyzer |
| 根 Directory.Build.targets | `Directory.Build.targets` | IsPackable/GenerateDocumentationFile 条件逻辑 |
| foundation Directory.Build.props | `foundation/Directory.Build.props` | 基础层配置 |
| infrastructure Directory.Build.props | `infrastructure/Directory.Build.props` | 基础设施层配置 |
| core Directory.Build.props | `core/Directory.Build.props` | 核心组件层配置 |
| services Directory.Build.props | `services/Directory.Build.props` | 服务组件层配置 |
| composition Directory.Build.props | `composition/Directory.Build.props` | 组合层配置 |
| app Directory.Build.props | `app/Directory.Build.props` | 主工程配置 |
| generators Directory.Build.props | `generators/Directory.Build.props` | 生成器：netstandard2.0 |
| tests Directory.Build.props | `tests/Directory.Build.props` | 测试：xUnit, Moq, FluentAssertions |
| global.json | `global.json` | .NET SDK 10.0.301 |
| nuget.config | `nuget.config` | NuGet 源 + 本地包目录 |
| Host GlobalUsings.cs | `app/JoinCode/GlobalUsings.cs` | Host 全局 Using |
| Abstractions/GlobalUsings.cs | `foundation/Abstractions/GlobalUsings.cs` | 全局 Using |
| Infrastructure/GlobalUsings.cs | `infrastructure/Infrastructure/GlobalUsings.cs` | 全局 Using |

## 12. 构建命令速查

```powershell
# 单组件快速编译
.\build.ps1 -Fast -SkipTests -Component Mcp

# 单组件单元测试
dotnet test "services/Mcp/tests/Unit/Mcp.Tests.csproj" -c Debug --filter "Category!=Integration"

# 全量编译+测试（提交前）
.\build.ps1 -Fast

# 仅生成器
.\build.ps1 -Fast -SkipTests -GeneratorsOnly

# 仅组件（不编译 Host）
.\build.ps1 -Fast -SkipTests -ComponentsOnly
```

## 13. 组件名→路径映射

| 组件名 | 路径 |
|--------|------|
| Abstractions | `foundation/Abstractions/` |
| Structura | `foundation/Structura/` |
| Transport.Contracts | `foundation/Transport.Contracts/` |
| Infrastructure | `infrastructure/Infrastructure/` |
| Transport.Impl | `infrastructure/Transport.Impl/` |
| Llm | `core/ai/Llm/` |
| Agents | `core/ai/Agents/` |
| Reasoning | `core/ai/Reasoning/` |
| Brain | `core/execution/Brain/` |
| Hands | `core/execution/Hands/` |
| McpToolDispatch | `core/execution/McpToolDispatch/` |
| Scheduling | `core/execution/Scheduling/` |
| Guard | `core/safety/Guard/` |
| Vault | `core/safety/Vault/` |
| CodeIndex | `core/search/CodeIndex/` |
| Browser | `core/search/Browser/` |
| Mcp | `services/Mcp/` |
| Dream | `services/Dream/` |
| Eyes | `services/Eyes/` |
| Bridge | `services/Bridge/` |
| SandboxSatellite | `services/SandboxSatellite/` |
| Composition | `composition/Composition/` |
| Clock | `composition/Clock/` |
| JoinCode | `app/JoinCode/` |
| Sdk | `app/Sdk/` |
