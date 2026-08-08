# JoinCode

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square)](https://dotnet.microsoft.com/)
[![NativeAOT](https://img.shields.io/badge/NativeAOT-Enabled-00A4EF?style=flat-square)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![C#](https://img.shields.io/badge/C%23-13-68217A?style=flat-square)](https://docs.microsoft.com/dotnet/csharp/)
[![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)](LICENSE)

**JoinCode** 是一个纯 C# 实现的开源 AI 编程智能体，运行在你的终端里，理解你的代码库，通过自然语言帮你编码更快——执行日常任务、解释复杂代码、处理 Git 工作流，全部一条命令搞定。

它编译为原生单文件可执行 `jcc.exe`，零运行时依赖，启动即达峰值性能。

> 💡 **为什么选 JoinCode？**
>
> - **🚀 原生性能** — NativeAOT 编译为单文件原生二进制，无 JIT、无 GC 暂停、无运行时依赖，冷启动毫秒级
> - **🧠 多模型适配** — DeepSeek / OpenAI / Anthropic / Azure 开箱即用，兼容 OpenAI API 协议
> - **🔧 丰富内置工具** — Shell 执行、文件操作、Web 请求、代码索引（TreeSitter AST）、浏览器自动化、技能系统
> - **🔌 MCP 协议** — 完整的 Model Context Protocol 客户端实现，无限扩展自定义工具
> - **🛡️ 生产级容错** — LLM 宽容处理（LlmJsonHelper 统一门控 + JSON 修复/参数归一化/类型转换/工具名归一化 + Trace 日志）、三级死循环干预、前缀缓存优化
> - **⚖️ 结构化推理** — `/falv` 三权分立推理引擎（控方→辩方→法官），DAG 证据链 + 双预算控制
> - **📦 零微软 AI 依赖** — 拒绝所有不支持 NativeAOT 的微软 AI SDK，从协议层自建 LLM 适配
> - **🖥️ 终端优先** — 为活在命令行里的开发者设计，交互式 REPL + 非交互式脚本双模式

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
| `JCC_API_KEY` | 是 | API 密钥 | `sk-xxxxxxxx` |
| `JCC_PROVIDER` | 否 | Provider 名称（默认 `deepseek`） | `deepseek` / `openai` / `anthropic` / `azure` |
| `JCC_MODEL_ID` | 否 | 模型 ID（默认 `deepseek-v4-flash`） | `deepseek-v4-flash` / `gpt-4o` / `claude-3-5-sonnet-20241022` |
| `JCC_ENDPOINT` | 否 | API 端点（默认使用 Provider 内置地址） | `http://localhost:9901` |

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
    "JCC_PROVIDER": "deepseek",
    "JCC_MODEL_ID": "deepseek-v4-flash"
  }
}
```

#### 可用模型

| 模型 ID | 别名 | 上下文 | 说明 |
|---------|------|--------|------|
| `deepseek-v4-flash` | `flash`、`v4`、`chat` | 1M | 快速模型，支持思考模式（默认） |
| `deepseek-v4-pro` | `pro` | 1M | 旗舰模型，支持思考模式 |

交互模式下可通过 `/model flash` 或 `/model pro` 快速切换模型。

#### API Key 优先级

从低到高：

1. `~/.jcc/auth.json` 中的 `"deepseek"` 字段
2. `JCC_API_KEY` 环境变量
3. `DEEPSEEK_API_KEY` 环境变量（最高优先级）
4. 回退：`OPENAI_API_KEY` 环境变量

### 1.4 运行

```powershell
# 非交互模式（单次对话，适合脚本集成）
jcc --trust -p "解释这个代码库的架构"

# 交互模式（REPL）
jcc --trust

# 指定模型
jcc --trust -m gpt-4o

# 查看帮助
jcc --help

# 诊断模式（输出详细日志）
$env:JCC_VERBOSE = "1"
jcc -p "你好"
```

### 1.5 常用斜杠命令

| 命令 | 说明 |
|------|------|
| `/help` | 查看所有命令 |
| `/model <name>` | 切换模型（如 `/model flash`、`/model pro`） |
| `/goal` | 目标设定（Outcome + Verification + Constraints） |
| `/falv` | 结构化推理（三权分立 + 证据链 + 双预算） |
| `/brief` | 简要模式 |
| `/clear` | 清空上下文 |
| `/rewind` | 撤回消息 |
| `/exit` | 退出 |

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

---

## 3. 架构与方法论

本工程对齐 Claude Code 和 DeepSeek-Reasonix，采用七层隔离架构 + 洋葱模型中间件管道。

1. **七层 slnx 隔离架构**：Generators → Foundation → Infrastructure → Core → Services → Composition → App，严格按依赖顺序编译，零循环依赖。每层独立解决方案，上层依赖下层的构建产物。
2. **洋葱模型 + 中间件管道**：按服务划分，嵌套中间件管道模型；为强调管道顺序，手动注册。共 14 条管道（Chat/Permission/Shell/Web/Skill 等）。
3. **源码生成器消除反射**：11 个 Generator（枚举元数据、构造函数注入、MCP 工具分发、AOT 安全分析等），编译期生成代码，运行时零反射。
4. **语法分析器纠正 LLM 行为**：TreeSitter AST 解析驱动代码理解，语法分析器能完成的事情不写入提示词。
5. **枚举唯一数据源**：有限集合的字符串常量必须枚举化 + `[EnumValue]`，源码生成器自动生成常量类和扩展方法，消费方零硬编码。

### 3.1 命令系统

#### 3.1.1 /goal 命令

```
/goal
目标 (Outcome)： [最终要达成的具体状态，最好有数字指标，如 p95 延迟降到 120ms 以下]
验证方式 (Verification)： [用什么命令或指标来证明完成，如 `npm test` 必须全通过]
硬性约束 (Constraints)： [整个过程中绝不能打破的底线，如不能改 `auth` 目录外的文件]
工作边界 (Boundaries)： [Codex 允许修改的文件或工具范围]
迭代与记录： [每次尝试后记录改动和结果（如更新 `EXPERIMENTS.md`）]
失败熔断： [如果遇到特定障碍无法推进，请停止并报告已尝试的路径和原因]
```

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

目前仅对齐 Claude Code 的记忆机制，因为发现了一个业界无解的级联记忆问题：

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

## 4. 技术要点

### 4.1 宽容处理

引入了 CommandCode 作者针对 DeepSeek 工具调用的容错方案，通过 `ToolCallRepairService` 实现多层容错机制，降低 LLM 工具调用出错概率：

#### 4.1.1 JSON 格式修复（RepairJson）

自动修复 LLM 返回的常见 JSON 格式问题：

| 问题类型 | 修复方式 | 示例 |
|----------|----------|------|
| 尾随逗号 | 移除对象/数组末尾多余逗号 | `{"a":1,}` → `{"a":1}` |
| 未加引号的键 | 自动添加双引号 | `{name:"test"}` → `{"name":"test"}` |
| 单引号键 | 转换为双引号 | `{'key':'value'}` → `{"key":"value"}` |
| 截断的 JSON | 自动闭合未关闭的字符串和括号 | `{"a":"test` → `{"a":"test"}` |

#### 4.1.2 参数名归一化（RepairArguments）

处理 LLM 返回的参数名与工具 Schema 不匹配的情况：

- **大小写不敏感匹配**：`FilePath` → `file_path`
- **别名映射**：`path` → `file_path`，`cmd` → `command`
- **snake_case/camelCase 自动转换**：`fileName` → `file_name`
- **优先级**：直接匹配 > 别名匹配 > 大小写匹配 > 格式转换

#### 4.1.3 参数类型自动转换（RepairArgumentTypes）

根据工具 Schema 的类型定义，自动转换参数类型：

- **字符串 → 整数**：`"42"` → `42`
- **字符串 → 数字**：`"3.14"` → `3.14`
- **字符串 → 布尔值**：`"true"` → `true`
- **字符串 → 数组**：`"[1,2,3]"` → `[1,2,3]`
- **数组 → 字符串**：`["text"]` → `"text"`

#### 4.1.4 工具名归一化（RepairToolName）

将 LLM 返回的任意大小写工具名归一化为标准名：

- 利用各工具名枚举的 `FromValue`（OrdinalIgnoreCase）反查
- 支持所有内置工具的大小写不敏感匹配
- 找不到匹配则返回原名（可能是 MCP 工具或自定义工具）

#### 4.1.5 LLM 结构化输出统一门控（LlmJsonHelper）

所有 LLM 返回的 JSON 处理必须通过 `LlmJsonHelper`，确保全局宽容处理一致。`ToolCallRepairService` 已收窄为 `internal`，外部禁止直接调用。

**结构化输出反序列化**（三层宽容策略）：

| 层级 | 策略 | 说明 |
|------|------|------|
| 第1层 | `ExtractJsonBlock` | 从 ` ```json ... ``` ` 代码块提取（大小写不敏感） |
| 第2层 | `ExtractInlineJson` / `ExtractArrayJson` | 从 `{...}` 或 `[...]` 提取内联 JSON |
| 第3层 | `RepairJson` | 调用 `ToolCallRepairService.RepairJson` 修复格式问题 |

**工具调用修复**（三个门控方法）：

| 方法 | 用途 | 触发 Trace 日志条件 |
|------|------|---------------------|
| `RepairJson(string?)` | JSON 格式修复（尾随逗号/未引号键/单引号/截断） | 修复成功且有 RepairHint |
| `RepairToolName(string?)` | 工具名归一化（大小写不敏感匹配） | 工具名被修改时 |
| `RepairArguments(name, dict, schema)` | 参数名归一化 + 参数类型自动转换 | 修复成功且有 RepairHint |

**使用方式**：

```csharp
// 引用类型（class）
var result = LlmJsonHelper.Deserialize(llmOutput, MyJsonContext.Default.MyType, out var repairHint);

// 数组类型（如 GraphDefineNode[]）
var nodes = LlmJsonHelper.DeserializeValue(nodesJson, GraphDefineJsonContext.Default.GraphDefineNodeArray, out _);

// 工具调用 JSON 修复
var repairResult = LlmJsonHelper.RepairJson(rawArguments);

// 工具名归一化
var normalizedName = LlmJsonHelper.RepairToolName(rawToolName);

// 参数名/类型修复
var argRepair = LlmJsonHelper.RepairArguments(toolName, arguments, handler.InputSchema);
```

**全局 JsonContext 宽容选项**：所有 `JsonSourceGenerationOptions` 统一配置三项宽容选项：

- `AllowTrailingCommas = true` — 容忍尾随逗号
- `ReadCommentHandling = JsonCommentHandling.Skip` — 跳过 JSON 注释
- `PropertyNameCaseInsensitive = true` — 属性名大小写不敏感

### 4.2 前缀缓存策略

对齐 DeepSeek-Reasonix 的部分亮点，通过多层机制确保前缀缓存命中，降低 token 消耗成本：

#### 4.2.1 系统提示词分区（SystemPromptBuilder）

将系统提示词分为静态前缀和动态后缀：

- **静态前缀**：会话期间保持不变的内容（如工具定义、核心指令），确保前缀缓存命中
- **动态后缀**：每轮可能变化的内容（如当前时间、会话状态），不影响静态前缀的缓存
- **分区构建**：通过 `BuildPartitioned()` 方法自动分离，标记 `CacheBreak=true` 的 section 进入动态后缀

#### 4.2.2 消息历史前缀保持

确保消息操作不破坏前缀缓存：

- **撤回操作**（`/rewind`）：移除尾部消息后，剩余消息必须是原始消息的前缀
- **追加日志**（AppendOnlyLog）：所有消息变更都保证前缀特性，避免缓存失效
- **自动压缩保护**：缓存命中时（`CacheReadInputTokens>0`）在 soft threshold（50%）~ 硬阈值（80%）之间推迟折叠（`Deferred`），达 `DeferFoldLimit` 次或缓存变冷才真正压缩，保护前缀缓存（对齐 Reasonix Go 版分层折叠）

#### 4.2.3 DeepSeek 缓存统计

支持 DeepSeek 特有的缓存统计字段：

- **prompt_cache_hit_tokens**：缓存命中 token 数
- **prompt_cache_miss_tokens**：缓存未命中 token 数
- **时间统计显示**：在 `[Timing]` 行中显示缓存命中情况（如 `缓存=命中120/未命中30`）

#### 4.2.4 设计目标

1. **成本优化**：通过前缀缓存减少重复 token 消耗
2. **会话一致性**：确保消息操作（撤回、压缩）不破坏缓存
3. **可观测性**：提供缓存命中统计，便于成本分析

### 4.3 死循环处理策略

#### 4.3.1 检测机制：OutputLoopDetector

基于滑动窗口的重复模式检测器，参数可配置：

- **窗口大小**：2000字符（检测最近2000字符的尾部）
- **模式长度范围**：10-500字符
- **重复次数阈值**：10次（同一模式连续出现10次视为循环）
- **检查间隔**：每50字符检查一次
- **冷却期**：500字符（检测到循环后暂停检测，避免频繁触发）

检测算法：从最大模式长度向最小模式长度遍历，检查文本尾部是否存在连续重复的模式。一旦检测到重复次数≥阈值，立即触发干预。

#### 4.3.2 干预机制：三级漏斗策略

通过 `LoopInterventionMiddleware` 实现渐进式干预：

| 级别 | 触发条件 | 干预动作 | 恢复策略 |
|------|----------|----------|----------|
| **Level 1** | 第1~2次检测到循环 | 软干预：注入提示词（"检测到输出可能陷入循环，请用序号→箭头方式总结当前回答再继续推理"），流继续 | - |
| **Level 2** | 第3~4次检测到循环 | 硬截断：撤回本轮对话 + 降低温度(0.6) + 重新发起LLM调用（最多2次重试） | 重试成功则继续；重试失败则升级到Level 3 |
| **Level 3** | 第5次+或重连失败 | 上下文压缩：自动压缩对话历史，保留最近1轮用户消息作为种子，无人值守恢复 | 压缩成功则继续；失败则重置到起点 |

#### 4.3.3 智能推进折扣

通过 `ITaskProgressTracker` 监控任务进度（如TODO完成情况），如果检测到循环期间任务有实际推进，则有效触发次数减少1（`ProgressDiscount`），降低干预级别，避免误伤正常推进的复杂任务。

#### 4.3.4 配置参数

```csharp
var options = LoopInterventionOptionsBuilder.Create()
    .WithHardTruncateThreshold(3)      // Level 2 触发阈值
    .WithCompactThreshold(5)           // Level 3 触发阈值
    .WithMaxRetryAttempts(2)           // Level 2 最大重试次数
    .WithRetryTemperature(0.6f)        // Level 2 重试温度
    .WithSecondChanceTemperature(0.3f) // Level 2 最后一次重试温度
    .WithProgressDiscount(1)           // 任务推进时的触发次数折扣
    .Build();
```

#### 4.3.5 设计理念

1. **渐进式干预**：从软提示到硬截断再到上下文压缩，逐步升级
2. **智能恢复**：通过降温和重连尝试打破循环，而非直接放弃
3. **任务感知**：考虑任务推进情况，避免打断正常工作的复杂任务
4. **无人值守**：Level 3压缩后自动恢复，无需用户干预
5. **审计追踪**：Level 2撤回时插入审计标记，便于问题排查

#### 4.3.6 模型层

1. 模型层用切片查看逻辑循环位置，回溯起因，然后微调输出，或通过稀疏自编码器对这部分权重加衰减惩罚。难度高，属于模型厂商工作，通常仅适合高频触发场景。
2. 用简单模型做检测，但部署和运行成本高。好处是拥有数据，投入下次模型训练后可更好地规避此类死循环。

### 4.4 并行动态负载

1. 必须改为 LINQ 链式调用。
2. 动态计算当前 CPU 负载并分级：90% 以上用 1 核心，70% 以上用一半核心，其余用全部核心。
3. 使用标准 System.Linq，通过 Directory.Build.props 全局引用。

### 4.5 串行编译

为防止多个 SubAgent 同时触发编译，从 bash 层拦截，统一加入 BuildQueue 队列排队执行，避免并行开发时因内存消耗导致卡死。

---

## 5. 小模型设计组合拳

上线的通常是小模型，这是出于成本考量。写思维链 CoT 通常无法成功诱导模型产出更高质量的对话，因为 LLM 本身过程可变。

必须打造一套组合拳，否则兜不住：
同义词转换 + 禁令 + 导向词 + 观察输出链给出反例 + 机械化 match 关键字二检。

### 5.1 同义词

让 LLM 理解自然语言到专业术语的映射，每次可存储到 CLAUDE.md 或某个 match 表。

### 5.2 禁令

禁令必须搭配导向，否则模型会发散到禁令以外的任何方向，后果很严重。

### 5.3 反例书写规则

必须先观察输出、复现问题，再写反例，再观察效果。压缩上下文时，确保整个任务单元已结束才可删除反例。若重复涉及同类型任务，通过 match 捕获后注入 rules。rules 本身应保持精简，否则每次压缩注入也会消耗上下文。

### 5.4 match 策略

尤其涉及退款订单号等场景，必须机械化二检，否则一个幻觉就糟糕了。通过正则表达式捕获关键字，强行结构化后传给工具。可以 fork 对话到临时上下文，让模型通过 JSON 结构化调用工具，保证查询的账号 + 商品订单号属于同一用户，否则给出不同错误提示；超过五次调用则判定对话熔断。

#### Q1：LLM 杜撰信息，不去调用工具怎么办？

A：分层强制

```
第一层：系统指令层（软约束）
· 在 system prompt 里写明："检测到订单号/账号时，必须调用 get_order_info 工具，否则回复将被拦截"。
· 同时给工具加上 "required": true 的显式标记（OpenAI/通义都支持）。

第二层：拦截层（硬兜底）——这才是真正管用的
· 正则二检在输出阶段会扫描模型生成的文本。
· 如果发现模型没调工具，却在文本里硬写了订单详情/退款结果：
  · 直接丢弃该回复。
  · 强制替换为："系统正在核实订单信息，请稍候..."（模板输出）。
  · 同时后台自动补齐工具调用，拿真实结果后再回复用户。

第三层：惩罚层（行为矫正）
· 统计该会话中"应调工具而未调"的次数。
· ≥2 次：该会话后续所有订单类问题，直接绕过模型，全程走模板流程，不再给模型调用工具的机会。
```

#### Q2：match 的关键字会非常厚

A：分层处理

```
第一层：核心硬关键词（极少，<20 个）
· 必须 100% 命中的：订单号、退款、账号、金额的正则模式。
· 这部分写死，永远在内存里。

第二层：业务扩展关键词（中等量，按需加载）
· 按业务场景拆成独立配置文件：refund_match.yaml、complaint_match.yaml。
· 按会话意图动态加载（比如用户第一句说了"退货"，只加载退货相关关键词库）。

第三层：模糊匹配层（AI 辅助生成）
· 线上日志里，把模型产生幻觉前的那句用户输入捞出来，跑一遍文本相似度聚类。
· 自动提取高频词，每周围绕 TOP5 新增关键词，而非一次性全写。
```

#### Q3：熔断 LLM 对话会造成用户体验不好

A：纵深防御工程处理 LLM 失败

- 第 1-2 次：正常提示——"订单信息暂时查询不到，请重新输入您的订单号，如果是杜撰的请调用工具查询用户最新订单号"
- 第 3 次：切换到选择题模式——"请问您的订单号是：A. [历史记录1] B. [历史记录2] C. 都不是"（用户只需点选，不再输入）
- 第 4 次：输出——"我们正在为您转接人工客服，预计等待1分钟..." + 后台预创建工单
- 第 5 次：不触发熔断，仅标记对话，人工直接介入接管

---

## 6. 项目架构索引

### 6.1 顶层目录

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

### 6.2 基础层（所有组件的公共依赖）

| 项目 | 路径 | 职责 | 关键 NuGet |
|------|------|------|-----------|
| **Abstractions** | `foundation/Abstractions/` | 纯接口 + DTO + 管道契约 + 特性标记（零实现） | Microsoft.Extensions.DI |
| **Infrastructure** | `infrastructure/Infrastructure/` | 管道核心/缓存/IO/遥测/本地化/SSH/插件 | YamlDotNet, Microsoft.Extensions.Hosting |

> **Abstractions** 内部按层分区：`00-core/`（Attributes, Configuration, Models, Pipeline, State...）、`01-ai/`（LLM, Mcp, Prompts）、`02-brain/`（Chat, Context, Query）、`03-hands/`（Shell, Skill, Tools）、`04-guard/`（Security）、`05-memory/`（Conversation, FileIO）、`06-perception/`（CodeIndex, Lsp, Web）、`07-agents/`（Agent, Team）、`08-transport/`（Bridge, Build）、`09-composition/`（Mode, Presentation）

### 6.3 组件依赖图（无环分层）

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

### 6.4 组件详情

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

### 6.5 组件内部结构

#### Brain (`02-brain/Brain/src/`)
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

#### Hands (`03-hands/Hands/src/`)
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

#### Guard (`04-guard/Guard/src/`)
```
Configuration/  配置加载
Hooks/          Hook 系统
OAuth/          OAuth 认证
Permission/     权限管理
Policy/         策略引擎
Security/       安全护栏
```

#### Vault (`05-memory/Vault/src/`)
```
Memdir/         记忆目录
Notification/   通知
State/          状态持久化
StepEvidence/   步骤证据
Todo/           待办事项
UserInteraction/ 用户交互
```

#### Mcp (`01-ai/Mcp/src/`)
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

#### Llm (`01-ai/Llm/src/`)
```
Adapters/       LLM 适配器（OpenAI/Anthropic/Azure/Pipe）
Registration/   注册服务
```

#### Reasoning (`10-reasoning/Reasoning/src/`)
```
Agents/         推理Agent基类(ReasoningAgent)+三权实现(控方/辩方/法官)
Engine/         推理引擎+配置+预算状态+摘要
Evidence/       数据项+证据+裁决
State/          枚举（状态/信任度/预设/续费方式）
DependencyInjection/ DI注册
```

### 6.6 源码生成器

| 生成器 | 路径 | 用途 | 使用范围 |
|--------|------|------|---------|
| AotSafety.Generator | `generators/AotSafety.Generator/` | AOT 安全分析器 | 全局（根 Directory.Build.props） |
| CodeFixes | `generators/CodeFixes/` | JCC 代码修复 | 全局（根 Directory.Build.props） |
| EnumMetadata.Generator | `generators/EnumMetadata.Generator/` | 枚举元数据（[EnumValue] → XxxConstants + XxxExtensions）+ SettingsMerge | 几乎所有组件 |
| McpToolDispatch.Generator | `generators/McpToolDispatch.Generator/` | MCP 工具处理器注册 + [Register] DI 注册 + Command 注册 | McpToolDispatch, Agents, Composition, Dream, JoinCode 及所有用 [Register] 的组件 |
| PromptSection.Generator | `generators/PromptSection.Generator/` | 提示词段落生成 | Brain |
| PromptTemplate.Generator | `generators/PromptTemplate.Generator/` | 提示词模板生成 | Brain |
| ToolPrompt.Generator | `generators/ToolPrompt.Generator/` | 工具提示生成 | Hands |
| CliOption.Generator | `generators/CliOption.Generator/` | CLI 选项绑定 | Bridge, Dream, JoinCode |
| AppModule.Generator | `generators/AppModule.Generator/` | 应用模块注册 | JoinCode |

### 6.7 Host 项目 (`app/JoinCode/`)

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

### 6.8 测试结构

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

### 6.9 SDK 聚合包 (`app/Sdk/`)

一行代码引用所有组件：`JoinCode.Sdk` 引用 Abstractions + Infrastructure + 全部组件

### 6.10 中间件管道清单

| 管道 | 接口 | 子系统 | 中间件链 |
|------|------|--------|---------|
| Chat | `StreamMiddlewarePipeline<ChatMiddlewareContext, ChatStreamEvent>` | Brain | Timing→ErrorHandling→AuditLog→TokenBudget→PreChat→QueryLoop→LoopIntervention→ProcessUsage→CleanupInjections→SaveContext |
| ChatInit | `MiddlewarePipeline<ChatInitContext>` | Brain | ContextLoad→CostRestore→ConfigChangeStart→SessionStartHook |
| ChatAdmin | `MiddlewarePipeline<ChatAdminContext>` | Brain | SessionAdmin→SessionSave |
| Preprocess | `MiddlewarePipeline<PreprocessContext>` | Brain | KeywordInjection→SynonymInjection→SystemPrompt→ReminderInjection→ToolListingInjection→LspDiagnostic |
| Compact | `MiddlewarePipeline<CompactContext>` | Brain | CompactHook→ContextCollapse→Microcompact→SessionMemoryCompact→ReactiveCompact |
| Query | `MiddlewarePipeline<QueryMiddlewareContext>` | Brain | UsdBudget→QueryTokenBudget→CostTracking→DiminishingReturns→HistorySnip→IdleReminder→StopHook→StateTransition→ContentReplacement |
| Permission | `MiddlewarePipeline<PermissionCheckContext>` | Guard | Bypass→AgentRestriction→AutoClassifier→ConfigGetOperation→WebFetchPermission→EarlyPathDeny→ToolListPermission→PathPermission→DangerousOperation→PlanMode→AutoSafety→DefaultResult |
| Settings | `MiddlewarePipeline<SettingsContext>` | Guard | SettingsReload→EffortLevel→HookRefresh→PermissionCache |
| AgentSpawn | `MiddlewarePipeline<AgentSpawnContext>` | Agents | DefinitionResolution→PromptBuilding→ContextSetup→AgentWorktreeSpawn→HookSetup→McpSetup→Metadata→Transcript |
| Fork | `MiddlewarePipeline<ForkContext>` | Agents | ForkValidation→ForkSpawn→ForkPermission→ForkExecution |
| Web | `MiddlewarePipeline<WebContext>` | Hands | Metrics→Validation→SsrfGuard→CacheCheck→DomainCheck→Fetch→ContentProcessing→CacheWrite |
| Shell | `MiddlewarePipeline<ShellContext>` | Hands | Validation→Classification→SedIntercept→Background→BuildIntercept→Execution→Output |
| Skill | `MiddlewarePipeline<SkillContext>` | Hands | Metrics→Validation→Telemetry→Execution |
| Code | `MiddlewarePipeline<CodeContext>` | Hands | Cache→Security→Llm→Sandbox→Metrics |

### 6.11 关键配置文件

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

### 6.12 构建命令速查

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

### 6.13 组件名→路径映射

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

---

## 7. 鸣谢与联系

- **鸣谢**：字节 TraeCN、华为 CodeArts
- **邮箱**：superhong@foxmail.com
