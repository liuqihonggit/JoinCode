# JoinCode

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square)](https://dotnet.microsoft.com/)
[![NativeAOT](https://img.shields.io/badge/NativeAOT-Enabled-00A4EF?style=flat-square)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![C#](https://img.shields.io/badge/C%23-13-68217A?style=flat-square)](https://docs.microsoft.com/dotnet/csharp/)
[![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)](../../LICENSE)

> 🌐 **语言：** [English](../../README.md) | **简体中文**

**JoinCode** 是一个纯 C# 实现的开源 AI 编程智能体，运行在你的终端里，理解你的代码库，通过自然语言帮你编码更快——执行日常任务、解释复杂代码、处理 Git 工作流，全部一条命令搞定。

它编译为原生单文件可执行 `jcc.exe`，零运行时依赖，启动即达峰值性能。

> 💡 **为什么选 JoinCode？**
>
> - **🚀 原生性能** — NativeAOT 编译为单文件原生二进制，无 JIT、无 GC 暂停、无运行时依赖，冷启动毫秒级
> - **🧠 多模型适配** — DeepSeek / OpenAI / Anthropic / Azure / SenseNova / Agnes 开箱即用，兼容 OpenAI Chat Completions / Anthropic Messages / OpenAI Responses 三种协议
> - **🔧 丰富内置工具** — Shell 执行、文件操作、Web 请求、代码索引（TreeSitter AST）、浏览器自动化、技能系统
> - **🔌 MCP 协议** — 完整的 Model Context Protocol 客户端实现，两阶段工具加载（core_tools/mcp_tools 分组按需拉取），无限扩展自定义工具
> - **🛡️ 生产级容错** — LLM 宽容处理（LlmJsonHelper 统一门控 + JSON 修复/参数归一化/类型转换/工具名归一化 + Trace 日志）、三级死循环干预、前缀缓存优化、工具惯性错误修正体系（gh 命令统一执行器 + Shell 管道自动改写 + 错误达阈值自动触发修正 Hook）
> - **⚖️ 结构化推理** — `/falv` 三权分立推理引擎（控方→辩方→法官），DAG 证据链 + 双预算控制
> - **🎯 多 Agent 协作** — `/goal` 多 Agent 任务图引擎，热点识别取代文件锁，22 组件防冲突改造
> - **🖥️ 多模式界面** — CLI 交互式 REPL + 非交互式脚本 + TUI 全屏界面（Terminal.Gui v2，多行输入 + Editor 组件 + 斜杠命令转发到底层 CmdMap）
> - **📦 零微软 AI 依赖** — 拒绝所有不支持 NativeAOT 的微软 AI SDK，从协议层自建 LLM 适配

---

## 1. 快速上手

### 1.1 环境要求

- **.NET 10 SDK**（10.0.301+）
- **Windows / Linux / macOS**（NativeAOT 全平台编译）

### 1.2 安装

```powershell
# 克隆仓库
git clone <repo-url>
cd JoinCode

# 七层顺序编译（Release 模式，自动启用 NativeAOT）
dotnet build Generators.slnx -c Release --no-incremental
dotnet build Foundation.slnx -c Release --no-incremental
dotnet build Infrastructure.slnx -c Release --no-incremental
dotnet build Core.slnx -c Release --no-incremental
dotnet build Services.slnx -c Release --no-incremental
dotnet build Composition.slnx -c Release --no-incremental
dotnet build App.slnx -c Release --no-incremental

# 或使用构建脚本一键编译
.\build.ps1 -Mode Fast -SkipTests -Configuration Release
```

编译产物位于 `artifacts/bin/JoinCode/Release/net10.0/jcc.exe`，单文件原生二进制，可直接分发。

### 1.3 认证配置

通过环境变量配置 LLM Provider：

| 环境变量 | 必填 | 说明 | 示例 |
|----------|------|------|------|
| `JCC_VENDOR` | 否 | 供应商名称（默认 `deepseek`） | `deepseek` / `openai` / `anthropic` / `azure` / `sensenova` / `agnes` |
| `JCC_MODEL_ID` | 否 | 模型 ID（默认 `deepseek-v4-flash`） | `deepseek-v4-flash` / `gpt-4o` / `claude-opus-5-20250815` / `sensenova-6.7-flash-lite` / `agnes-2.0-flash` |
| `JCC_ENDPOINT` | 否 | API 端点（默认使用 Provider 内置地址） | `http://localhost:9901` |

各供应商对应的 API Key 环境变量：

| 供应商 | 环境变量 | 默认端点 |
|--------|----------|----------|
| `deepseek` | `DEEPSEEK_API_KEY` | 内置（兼容 OpenAI 协议） |
| `openai` | `OPENAI_API_KEY` | `https://api.openai.com/v1` |
| `anthropic` | `ANTHROPIC_API_KEY` | `https://token.sensenova.cn/v1`（可经 SenseNova 中转） |
| `azure` | `AZURE_OPENAI_API_KEY` | 需用户配置（Azure OpenAI 资源地址） |
| `sensenova` | `SENSENOVA_API_KEY` | `https://token.sensenova.cn/v1` |
| `agnes` | `AGNES_API_KEY` | `https://apihub.agnes-ai.com/v1` |

#### 使用 DeepSeek（推荐）

DeepSeek 是默认 Provider，兼容 OpenAI API 协议，开箱即用：

```powershell
# 设置 API Key
$env:DEEPSEEK_API_KEY = "sk-your-deepseek-api-key"

# 启动交互式 REPL
jcc --trust
```

> **回退机制**：未设置 `DEEPSEEK_API_KEY` 时，自动回退读取 `OPENAI_API_KEY`。

#### 方式二：配置文件

将 API Key 存储到 `~/.jcc/auth.json`，避免每次设置环境变量：

```json
{
  "deepseek": "sk-your-deepseek-api-key"
}
```

将 Provider 设置写入 `~/.jcc/settings.json`：

```json
{
  "provider": "deepseek",
  "modelId": "deepseek-v4-flash"
}
```

#### 方式三：项目级配置

在项目根目录创建 `.env/api.json`（适合团队共享默认配置）：

```json
{
  "env": {
    "DEEPSEEK_API_KEY": "sk-your-deepseek-api-key",
    "JCC_VENDOR": "deepseek",
    "JCC_MODEL_ID": "deepseek-v4-flash"
  }
}
```

#### 可用模型

预置 5 个供应商共 41 个模型条目（跨供应商去重后 40 个独立模型）。详细的模型列表、别名、上下文长度见 [可用模型列表](../reference/models.md)。

交互模式下可通过 `/model <别名或ID>` 快速切换模型，例如 `/model flash`、`/model pro`、`/model opus5`、`/model sonnet`、`/model 5.6`。

#### API Key 优先级

从低到高：

1. `~/.jcc/auth.json` 中的 `"deepseek"` 字段
2. `DEEPSEEK_API_KEY` 环境变量（最高优先级）
3. 回退：`OPENAI_API_KEY` 环境变量

### 1.4 运行

```powershell
# 非交互模式（单次对话，适合脚本集成）
jcc --trust -p "解释这个代码库的架构"

# 交互模式（REPL）
jcc --trust

# TUI 模式（Terminal.Gui v2 全屏交互界面，支持多行输入、Editor 组件、斜杠命令转发）
jcc --trust --tui

# 指定模型
jcc --trust -m gpt-4o

# 跳过所有权限检查（替代旧 --dangerously-skip-permissions，等价于 --permission-mode bypass）
jcc --bypass -p "批量重构"

# 查看帮助
jcc --help

# 诊断模式（输出详细日志 [WIRE] [STEP] [READY] 等）
jcc --debuglog -p "你好"
# 或通过环境变量
$env:JCC_DEBUGLOG = "1"
jcc -p "你好"
```

**常用 CLI 参数速查**：

| 参数 | 说明 |
|------|------|
| `--trust` | 信任当前目录（跳过目录信任确认） |
| `-p / --prompt <text>` | 非交互模式单次对话 |
| `-m / --model <id>` | 指定模型 ID 或别名 |
| `--tui` | 启动 TUI 全屏界面（Terminal.Gui v2） |
| `--bypass` | 跳过所有权限检查（等价 `--permission-mode bypass`，替代旧 `--dangerously-skip-permissions`） |
| `--permission-mode <mode>` | 权限模式：`plan` / `auto` / `ask` / `bypass` |
| `--debuglog` / `-d` | 启用调试日志（等效 `JCC_DEBUGLOG=1`） |
| `--await <seconds>` | 非交互模式超时自动关闭（超时返回 1234） |
| `--doctor` | 医生模式：监控病人进程并自动修复 |
| `--non-interactive` | 从 stdin 读取，输出到 stdout |

### 1.5 常用斜杠命令

**会话与聊天：**

| 命令 | 说明 |
|------|------|
| `/help` | 查看所有命令 |
| `/clear` | 清空聊天历史（别名：`reset`、`new`、`cls`） |
| `/compact` | 压缩对话上下文以节省 Token（别名：`comp`） |
| `/rewind` | 恢复代码和/或对话到之前的状态（别名：`checkpoint`） |
| `/fork` | 创建当前对话的分支（别名：`branch`） |
| `/resume` | 恢复之前的会话（别名：`continue`） |
| `/exit` | 退出（别名：`x`） |

**模型与供应商：**

| 命令 | 说明 |
|------|------|
| `/model <name>` | 切换模型（如 `/model flash`、`/model pro`） |
| `/vendor <name>` | 切换 LLM 供应商（`openai` / `anthropic` / `deepseek` / …） |
| `/sampling` | 查看或设置采样参数（温度/最大Token） |
| `/effort` | 调整推理力度（`low` / `medium` / `high` / `max` / `auto`） |

**Agent 与推理：**

| 命令 | 说明 |
|------|------|
| `/goal` | 目标自主循环引擎（Outcome + Verification + Constraints） |
| `/falv` | 结构化推理（三权分立 + 证据链 + 双预算） |
| `/plan` | 计划模式管理 |
| `/agents` | 查看和管理代理 |
| `/tasks` | 列出和管理后台任务 |

**工具与配置：**

| 命令 | 说明 |
|------|------|
| `/mcp` | 管理 MCP 服务器 |
| `/config` | 管理配置设置 |
| `/permissions` | 管理权限规则 |
| `/tools` | 显示可用工具列表 |
| `/init` | AI驱动初始化项目配置文件 |
| `/doctor` | 诊断环境配置和依赖（别名：`dr`） |

**信息：**

| 命令 | 说明 |
|------|------|
| `/status` | 显示版本、模型、账户、API连接和工具状态 |
| `/cost` | 显示使用成本统计 |
| `/stats` | 查看会话统计 |
| `/context` | 显示当前会话上下文统计 |

> 在 JoinCode 中运行 `/help` 查看完整的 80+ 命令列表。

---

## 2. 核心特性

### 2.1 代码理解与生成

- 查询和编辑大型代码库，3313 个文件 AST 解析仅需 ~2.7 秒
- TreeSitter 语法分析驱动的代码索引，增量 AST 无需持久化
- 调试问题、排查故障，用自然语言描述即可

### 2.2 自动化与集成

- 自动化运维任务——查询 PR、处理复杂 rebase、批量重构
- MCP 服务器连接自定义能力——工具、技能、工作流无限扩展
- 非交互模式 `jcc -p "..."` 集成到 CI/CD 脚本

### 2.3 生产级容错

- **LLM 宽容处理**：JSON 格式修复、参数名归一化、参数类型自动转换、工具名归一化
- **死循环三级干预**：软提示 → 硬截断 + 降温重试 → 上下文压缩 + 无人值守恢复
- **前缀缓存优化**：系统提示词分区 + 消息历史前缀保持 + DeepSeek 缓存统计
- **智能推进折扣**：检测到任务有实际推进时降低干预级别，避免误伤

### 2.4 结构化推理

- `/falv` 命令启动三权分立推理引擎：控方（收集证据）→ 辩方（质疑反驳）→ 法官（裁决）
- DAG 证据链 + 双预算控制（轮次预算 + Token 预算，谁先触底谁停止）
- 三级证明标准：Murder（排除合理怀疑）/ Panda（视情节浮动）/ Divorce（高度盖然性）
- `/falv --continue` 续费续命，继续推理

### 2.5 安全与权限

- 多层权限管道：路径权限 → 危险操作拦截 → 自动安全分类 → Agent 限制
- OAuth 认证、Hook 系统、策略引擎
- Doctor 模式（`--doctor`）监控病人进程，自动修复问题

### 2.6 原生性能

- NativeAOT 编译为单文件原生二进制，零运行时依赖
- 9 个源码生成器消除运行时反射：枚举元数据、MCP 工具分发（含 [Register] DI 注册）、提示词段落/模板、工具提示、AOT 安全分析……
- 七层 slnx 隔离架构，严格按依赖顺序编译，零循环依赖
- 14 条中间件管道（Chat/Permission/Shell/Web/Skill……），洋葱模型 + 手动注册强调顺序

### 2.7 多 Agent 协作（PR #121）

- **`/goal` 任务图引擎**：基于 PRD v2.1 改造为多 Agent 协作任务图，复用 team MCP 共享组件
- **热点识别取代文件锁**：`HotFileDetector` + `IntentCollector` + `HotSpotTracker` 识别冲突文件，`HotSpotResolutionPolicy` 决策
- **契约变更广播**：`ContractChangeBroadcaster` + `ContractChangeNotificationRouter` 桥接邮箱到 Worker 队列，软通知模式含 `git pull` 提示
- **队长派发 + 合并队列**：`CaptainDispatchGuard` + `CallSiteFinder` + `MergeQueueService` + `DeferredMailService`
- 22 个纯新增组件 + 4 个集成任务 + 7 个断裂点修复，3300+ 测试零破坏

### 2.8 TUI 全屏界面（PR #110/#116/#118）

- **Terminal.Gui v2 声明式布局**：`--tui` 启动，7 个视图组件（RootView/PromptView/OutputView/StatusBarView/QueuedCommandsView/AgentPanesView/PermissionDialogView）
- **Editor 组件多行输入**：`Ctrl+Enter` 发送，`Enter` 换行，`Ctrl+Up/Down` 历史导航，输出区支持文本选择复制 + WordWrap 软换行 + 行级环形缓冲（默认 2048 行可配置）
- **斜杠命令统一转发**：TUI 不再有两套命令系统，全部转发到底层 `CmdMap`（`ChatCommandRegistry` + `CmdMap` + `CommandServices`），`TabCompleter` 从源码生成器获取命令列表
- **性能优化**：启动并行化降 37%，去反射扫描启动 -70%，全管线 UTF-8 + batch mode 写终端 -83%，`LayoutAndDraw` -58%

### 2.9 LLM 协议与工具加载（PR #122）

- **三协议支持**：`openai-compatible`（Chat Completions）/ `anthropic`（Messages）/ `responses`（OpenAI Responses API，`JCC_PROTOCOL=responses` 走 `/responses` 端点）
- **两阶段工具加载**：`McpToolBridge` 按 `ToolKind` 分组（core_tools/mcp_tools），QueryService 检测 `tool_description_request` 并发送第二次请求，系统提示词告知 LLM 按需加载
- **DeepSeek thinking 模式**：`ChatOptions.ThinkingEnabled` → `OpenAIChatRequest.thinking` 字段，settings.json `alwaysThinkingEnabled` 配置或环境变量启用
- **AnthropicCompatible 通用类**：支持任意供应商走 Anthropic 协议（如 SenseNova 中转 Claude）
- **子代理模型 inherit 关键字**：`JCC_SUBAGENT_MODEL` 环境变量 > `SpawnOptions.Model` > `Definition.ModelName` > `inherit`/父级模型，Bedrock 跨区域前缀继承

### 2.10 工具惯性错误修正体系（PR #119）

- **gh 命令统一执行器**：`IGitHubCommandRunner` + `GitHubCommandRunner`（含重试）
- **命令改写器**：`ICommandRewriter` + `CommandRewriterRegistry`，Shell 管道自动改写 gh 命令（`HeredocRewriter` 优先级 200 自动检测 HEREDOC 并转换为双引号字符串）
- **工具修正 Hook**：`IToolFixHook` + `ToolFixHookRegistry`，错误达阈值自动触发修正器
- **模型 ID 错误记录**：`ToolHealthRecord` 添加 `ModelId` 字段，按模型 ID 过滤工具健康度

---

## 3. 架构与方法论

本工程对齐 TS 原版 和 DeepSeek-Reasonix，采用七层隔离架构 + 洋葱模型中间件管道。

1. **七层 slnx 隔离架构**：Generators → Foundation → Infrastructure → Core → Services → Composition → App，严格按依赖顺序编译，零循环依赖。每层独立解决方案，上层依赖下层的构建产物。
2. **洋葱模型 + 中间件管道**：按服务划分，嵌套中间件管道模型；为强调管道顺序，手动注册。共 14 条管道（Chat/Permission/Shell/Web/Skill 等）。
3. **源码生成器消除反射**：11 个 Generator（枚举元数据、构造函数注入、MCP 工具分发、AOT 安全分析等），编译期生成代码，运行时零反射。
4. **语法分析器纠正 LLM 行为**：TreeSitter AST 解析驱动代码理解，语法分析器能完成的事情不写入提示词。
5. **枚举唯一数据源**：有限集合的字符串常量必须枚举化 + `[EnumValue]`，源码生成器自动生成常量类和扩展方法，消费方零硬编码。

### 3.1 命令系统

#### 3.1.1 /goal 命令

`/goal` 已升级为**多 Agent 协作任务图引擎**（PR #121），基于 PRD v2.1 改造，复用 team MCP 共享组件：

```
/goal
目标 (Outcome)： [最终要达成的具体状态，最好有数字指标，如 p95 延迟降到 120ms 以下]
验证方式 (Verification)： [用什么命令或指标来证明完成，如 `npm test` 必须全通过]
硬性约束 (Constraints)： [整个过程中绝不能打破的底线，如不能改 `auth` 目录外的文件]
工作边界 (Boundaries)： [Codex 允许修改的文件或工具范围]
迭代与记录： [每次尝试后记录改动和结果（如更新 `EXPERIMENTS.md`）]
失败熔断： [如果遇到特定障碍无法推进，请停止并报告已尝试的路径和原因]
```

**多 Agent 协作机制**：

- **热点识别取代文件锁**：`HotFileDetector` 识别冲突文件，`IntentCollector` 收集修改意图，`HotSpotTracker` 追踪热点，`HotSpotResolutionPolicy` 决策冲突解决策略
- **契约变更广播**：`ContractChangeBroadcaster` 广播契约变更，`ContractChangeNotificationRouter` 桥接邮箱到 Worker 队列，软通知模式含 `git pull` 提示
- **队长派发 + 合并队列**：`CaptainDispatchGuard` 守卫队长派发，`CallSiteFinder` 定位调用点，`MergeQueueService` 合并队列，`DeferredMailService` 延迟邮件
- **22 组件防冲突**：17 个纯新增组件 + 4 个集成任务 + 7 个断裂点修复，3300+ 测试零破坏

#### 3.1.2 /falv 命令

结构化推理引擎 — 假定→验证→事实 三权分立，基于 DAG 证据链：

- **三权分立**：控方（收集证据）→ 辩方（质疑反驳）→ 法官（裁决）
- **双预算控制**：轮次预算 + Token 预算，谁先触底谁停止
- **续费续命**：`/falv --continue [rounds|tokens|both|default]` 续费并继续推理
- **三级证明标准**：Murder（杀人罪，排除合理怀疑）/ Panda（吃熊猫罪，视情节浮动）/ Divorce（离婚官司，高度盖然性）

```
/falv <假定内容>                          添加假定
/falv --status                           查看推理状态+预算
/falv --judge                            触发三权裁决
/falv --evidence                         查看证据链
/falv --continue [rounds|tokens|both|default]  续费并继续
/falv --budget                           查看预算状态
```

#### 3.1.3 待实现

- 对标 MoA（Mixture of Agents）功能——三个臭皮匠胜过诸葛亮。
- `/bug` 命令：采用多个 subAgent 并行修复，防止单个 subAgent 无法命中问题。配置多个 `API KEY` 使用不同 LLM 模型或许更佳。

```
/bug 依据文档要求,修复xx的bug,它的表现是...
```

### 3.2 测试策略

主要依赖单元测试 + 语法分析器 + E2E 测试，通过真实 mock 服务端进行真实启动执行。这样可以进行真实对话，验证前缀缓存是否生效。

### 3.3 记忆系统

目前仅对齐 TS 原版 的记忆机制，因为发现了一个业界无解的级联记忆问题：

```
1. 我距离家到公司需要走20分钟。
   ……中间记忆……
9999. 今天我搬家了，现在距离地铁口30分钟。

此时再问 AI："我目前需要走回家多久？"
a. 搬家之后并没有让它重新获取信息；
b. 它也不可能实时修正全部历史，因为关联历史是海量的。
它要么回答旧信息，要么幻觉，要么"不知道"，要么调查定位之后分析。
```

OpenAI 的多层记忆 + 半衰期方案更为合理，但实现难度太高，暂时搁置。

---

## 4. 详细文档

| 文档 | 说明 |
|------|------|
| [可用模型列表](../reference/models.md) | 5 个供应商 41 个模型的详细列表（别名、上下文长度、说明） |
| [技术要点](../design/technical-details.md) | 宽容处理 / 前缀缓存 / 死循环处理 / 并行动态负载 / 串行编译 |
| [小模型设计组合拳](../design/small-model-strategy.md) | 面向小模型场景的工程化策略（同义词 / 禁令 / 反例 / match） |
| [项目架构索引](../design/architecture-index.md) | 组件依赖图 / 详情表 / 内部结构 / 中间件管道清单 / 构建命令速查 |
| [架构决策记录](../adr/README.md) | 40+ ADR：为什么选 A 而不选 B |

---

## 5. 鸣谢与联系

- **鸣谢**：字节 TraeCN、华为 CodeArts
- **邮箱**：superhong@foxmail.com
