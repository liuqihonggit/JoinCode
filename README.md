# JoinCode

一个看起来是专为 C# 打造的 AI 编程助手，其实是通用哒。

最初目标是实现无人值守的自动迁移功能，后来发现这条路走不通。

其次，针对 DeepSeek 频繁出现的死循环问题，打造了一套组合拳来应对。

最终目标是实现纯 C# 全栈 Agent——为了支持 AOT 编译，抛弃了微软的全部 SDK。

我发现构建 3313 个文件的 AST 解析仅需约 2.7 秒，增量 AST 在修改期间速度更快，这样无需持久化，检索时大幅节省 token。

## 架构和方法论

本工程主要对齐 Claude Code 和 DeepSeek-Reasonix。

1. 项目体量巨大，曾尝试用单个 slnx 一把梭，结果每次单元测试约 8000 个，编译和测试时电脑堪比拖拉机。
   因此必须拆分为多个组件，`components/` 内各独立文件夹相当于子模块，尽可能解耦。
   项目规模逐渐失控，不得不将 SDK 单独抽离。

2. git worktree 每次编译会将包隔离到分支文件夹，这点很好，但每个文件夹就占约 2G。

3. 硬编码字符串（尤其是工具类型）推荐改为枚举，项目内部已做了少量双语全球化。

4. 项目采用语法分析器纠正 LLM 行为。

5. 语法分析器能完成的事情，就不要写到 CLAUDE.md。

6. 洋葱模型——按不同服务划分，嵌套中间件管道模型；为强调管道顺序，手动注册。

### 命令

#### /goal 命令

```
/goal
目标 (Outcome)： [最终要达成的具体状态，最好有数字指标，如 p95 延迟降到 120ms 以下]
验证方式 (Verification)： [用什么命令或指标来证明完成，如 `npm test` 必须全通过]
硬性约束 (Constraints)： [整个过程中绝不能打破的底线，如不能改 `auth` 目录外的文件]
工作边界 (Boundaries)： [Codex 允许修改的文件或工具范围]
迭代与记录： [每次尝试后记录改动和结果（如更新 `EXPERIMENTS.md`）]
失败熔断： [如果遇到特定障碍无法推进，请停止并报告已尝试的路径和原因]
```

#### 待实现

- 对标 MoA（Mixture of Agents）功能——三个臭皮匠胜过诸葛亮。
- `/bug` 命令：采用多个 subAgent 并行修复，防止单个 subAgent 无法命中问题。配置多个 `API KEY` 使用不同 LLM 模型或许更佳。

```
/bug 依据文档要求,修复xx的bug,它的表现是...
```

### 测试

实际上从未用 Visual Studio 启动过项目进行测试，主要依赖单元测试 + 语法分析器 + E2E 测试，通过真实 mock 服务端进行真实启动执行。这样可以进行真实对话，验证前缀缓存是否生效。

### 记忆

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

我认为 OpenAI 的多层记忆 + 半衰期方案更为合理……但实现难度太高，暂时搁置。



# 技术要点

## 0x01 宽容处理

引入了 CommandCode 作者针对 DeepSeek 工具调用的容错方案，通过 `ToolCallRepairService` 实现多层容错机制，降低 LLM 工具调用出错概率：

### JSON 格式修复（RepairJson）

自动修复 LLM 返回的常见 JSON 格式问题：

| 问题类型 | 修复方式 | 示例 |
|----------|----------|------|
| 尾随逗号 | 移除对象/数组末尾多余逗号 | `{"a":1,}` → `{"a":1}` |
| 未加引号的键 | 自动添加双引号 | `{name:"test"}` → `{"name":"test"}` |
| 单引号键 | 转换为双引号 | `{'key':'value'}` → `{"key":"value"}` |
| 截断的 JSON | 自动闭合未关闭的字符串和括号 | `{"a":"test` → `{"a":"test"}` |

### 参数名归一化（RepairArguments）

处理 LLM 返回的参数名与工具 Schema 不匹配的情况：

- **大小写不敏感匹配**：`FilePath` → `file_path`
- **别名映射**：`path` → `file_path`，`cmd` → `command`
- **snake_case/camelCase 自动转换**：`fileName` → `file_name`
- **优先级**：直接匹配 > 别名匹配 > 大小写匹配 > 格式转换

### 参数类型自动转换（RepairArgumentTypes）

根据工具 Schema 的类型定义，自动转换参数类型：

- **字符串 → 整数**：`"42"` → `42`
- **字符串 → 数字**：`"3.14"` → `3.14`
- **字符串 → 布尔值**：`"true"` → `true`
- **字符串 → 数组**：`"[1,2,3]"` → `[1,2,3]`
- **数组 → 字符串**：`["text"]` → `"text"`

### 工具名归一化（RepairToolName）

将 LLM 返回的任意大小写工具名归一化为标准名：

- 利用各工具名枚举的 `FromValue`（OrdinalIgnoreCase）反查
- 支持所有内置工具的大小写不敏感匹配
- 找不到匹配则返回原名（可能是 MCP 工具或自定义工具）

## 0x02 前缀缓存策略

对齐 DeepSeek-Reasonix 的部分亮点，通过多层机制确保前缀缓存命中，降低 token 消耗成本：

### 系统提示词分区（SystemPromptBuilder）

将系统提示词分为静态前缀和动态后缀：

- **静态前缀**：会话期间保持不变的内容（如工具定义、核心指令），确保前缀缓存命中
- **动态后缀**：每轮可能变化的内容（如当前时间、会话状态），不影响静态前缀的缓存
- **分区构建**：通过 `BuildPartitioned()` 方法自动分离，标记 `CacheBreak=true` 的 section 进入动态后缀

### 消息历史前缀保持

确保消息操作不破坏前缀缓存：

- **撤回操作**（`/rewind`）：移除尾部消息后，剩余消息必须是原始消息的前缀
- **追加日志**（AppendOnlyLog）：所有消息变更都保证前缀特性，避免缓存失效
- **自动压缩保护**：soft threshold（50%）和 hard threshold 之间不触发自动压缩，保护前缀缓存

### DeepSeek 缓存统计

支持 DeepSeek 特有的缓存统计字段：

- **prompt_cache_hit_tokens**：缓存命中 token 数
- **prompt_cache_miss_tokens**：缓存未命中 token 数
- **时间统计显示**：在 `[Timing]` 行中显示缓存命中情况（如 `缓存=命中120/未命中30`）

### 设计目标

1. **成本优化**：通过前缀缓存减少重复 token 消耗
2. **会话一致性**：确保消息操作（撤回、压缩）不破坏缓存
3. **可观测性**：提供缓存命中统计，便于成本分析



## 0x03 死循环处理策略

### 检测机制：OutputLoopDetector

基于滑动窗口的重复模式检测器，参数可配置：

- **窗口大小**：2000字符（检测最近2000字符的尾部）
- **模式长度范围**：10-500字符
- **重复次数阈值**：10次（同一模式连续出现10次视为循环）
- **检查间隔**：每50字符检查一次
- **冷却期**：500字符（检测到循环后暂停检测，避免频繁触发）

检测算法：从最大模式长度向最小模式长度遍历，检查文本尾部是否存在连续重复的模式。一旦检测到重复次数≥阈值，立即触发干预。

### 干预机制：三级漏斗策略

通过 `LoopInterventionMiddleware` 实现渐进式干预：

| 级别 | 触发条件 | 干预动作 | 恢复策略 |
|------|----------|----------|----------|
| **Level 1** | 第1~2次检测到循环 | 软干预：注入提示词（"检测到输出可能陷入循环，请用序号→箭头方式总结当前回答再继续推理"），流继续 | - |
| **Level 2** | 第3~4次检测到循环 | 硬截断：撤回本轮对话 + 降低温度(0.6) + 重新发起LLM调用（最多2次重试） | 重试成功则继续；重试失败则升级到Level 3 |
| **Level 3** | 第5次+或重连失败 | 上下文压缩：自动压缩对话历史，保留最近1轮用户消息作为种子，无人值守恢复 | 压缩成功则继续；失败则重置到起点 |

### 智能推进折扣

通过 `ITaskProgressTracker` 监控任务进度（如TODO完成情况），如果检测到循环期间任务有实际推进，则有效触发次数减少1（`ProgressDiscount`），降低干预级别，避免误伤正常推进的复杂任务。

### 配置参数

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

### 设计理念

1. **渐进式干预**：从软提示到硬截断再到上下文压缩，逐步升级
2. **智能恢复**：通过降温和重连尝试打破循环，而非直接放弃
3. **任务感知**：考虑任务推进情况，避免打断正常工作的复杂任务
4. **无人值守**：Level 3压缩后自动恢复，无需用户干预
5. **审计追踪**：Level 2撤回时插入审计标记，便于问题排查

### 模型层

1. 模型层用切片查看逻辑循环位置，回溯起因，然后微调输出，或通过稀疏自编码器对这部分权重加衰减惩罚。难度高，属于模型厂商工作，通常仅适合高频触发场景。

2. 用简单模型做检测，但部署和运行成本高。好处是拥有数据，投入下次模型训练后可更好地规避此类死循环。



## 0x04 并行动态负载

1. 必须改为 LINQ 链式调用。
2. 动态计算当前 CPU 负载并分级：90% 以上用 1 核心，70% 以上用一半核心，其余用全部核心。
3. 使用标准 System.Linq，通过 Directory.Build.props 全局引用。

## 0x05 串行编译

为防止多个 SubAgent 同时触发编译，从 bash 层拦截，统一加入 BuildQueue 队列排队执行，避免并行开发时因内存消耗导致卡死。




# 鸣谢

- 字节 TraeCN
- 华为 CodeArts



# 小模型设计组合拳

上线的通常是小模型，这是出于成本考量。写思维链 CoT 通常无法成功诱导模型产出更高质量的对话，因为 LLM 本身过程可变。

必须打造一套组合拳，否则兜不住：
同义词转换 + 禁令 + 导向词 + 观察输出链给出反例 + 机械化 match 关键字二检。

## 1. 同义词

让 LLM 理解自然语言到专业术语的映射，每次可存储到 CLAUDE.md 或某个 match 表。

## 2. 禁令

禁令必须搭配导向，否则模型会发散到禁令以外的任何方向，后果很严重。

## 3. 反例书写规则

必须先观察输出、复现问题，再写反例，再观察效果。压缩上下文时，确保整个任务单元已结束才可删除反例。若重复涉及同类型任务，通过 match 捕获后注入 rules。rules 本身应保持精简，否则每次压缩注入也会消耗上下文。

## 4. match 策略

尤其涉及退款订单号等场景，必须机械化二检，否则一个幻觉就糟糕了。通过正则表达式捕获关键字，强行结构化后传给工具。可以 fork 对话到临时上下文，让模型通过 JSON 结构化调用工具，保证查询的账号 + 商品订单号属于同一用户，否则给出不同错误提示；超过五次调用则判定对话熔断。

### Q1：LLM 杜撰信息，不去调用工具怎么办？

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

### Q2：match 的关键字会非常厚

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

### Q3：熔断 LLM 对话会造成用户体验不好

A：惰性工程处理 LLM 失败

- 第 1-2 次：正常提示——"订单信息暂时查询不到，请重新输入您的订单号，如果是杜撰的请调用工具查询用户最新订单号"
- 第 3 次：切换到选择题模式——"请问您的订单号是：A. [历史记录1] B. [历史记录2] C. 都不是"（用户只需点选，不再输入）
- 第 4 次：输出——"我们正在为您转接人工客服，预计等待1分钟..." + 后台预创建工单
- 第 5 次：不触发熔断，仅标记对话，人工直接介入接管



# 联系

`tui/` 文件夹目前是坏的，并未接入 CLI，所以目前是纯 CLI 项目。而且发现 Claude 的 TUI 刷新也充满 bug，不太好模仿。免费的 AI 工具处理不了 TUI 的某些 bug……

到底有什么方法论可以直接让 AI 生成 TUI 呢？貌似要搭配录制 gif/jpg，还要配合模型……

因为蹭的是免费 AI，TraeCN 政策已从日限制改为周限制，所以这个工程会无限期延后……

不如……用 Avalonia 做 GUI？或者像 DeepSeek-Reasonix 那样用 Web？项目太大了，已经把控不住，以至于不想干了……

superhong@foxmail.com（虽然不一定回你）

---

# 项目架构索引

## 顶层目录

```
JoinCode/
├── components/          ★ 16 个业务组件源码（按层分目录）
├── generators/          ★ 9 个源码生成器（netstandard2.0）
├── sdk/                 ★ Abstractions（纯接口）+ Sdk（聚合包）
├── src/                 ★ JoinCode Host（jcc.exe）+ Infrastructure
├── tests/               ★ 单元/集成/MockServer/基准测试
├── tools/               ★ 辅助工具（AST审计/跨进程/注入迁移）
├── build.ps1            主构建脚本
├── JoinCode.slnx         主解决方案（Host + tests + MockServers）
├── components.slnx      组件解决方案（全部组件源码 + 组件测试）
├── generators.slnx      生成器解决方案
├── sdk.slnx             SDK 解决方案
└── tools.slnx           工具解决方案
```

## 基础层（所有组件的公共依赖）

| 项目 | 路径 | 职责 | 关键 NuGet |
|------|------|------|-----------|
| **Abstractions** | `sdk/Abstractions/` | 纯接口 + DTO + 管道契约 + 特性标记（零实现） | Microsoft.Extensions.DI |
| **Infrastructure** | `src/Infrastructure/` | 管道核心/缓存/IO/遥测/本地化/SSH/插件 | YamlDotNet, Microsoft.Extensions.Hosting |

> **Abstractions** 内部按层分区：`00-core/`（Attributes, Configuration, Models, Pipeline, State...）、`01-ai/`（LLM, Mcp, Prompts）、`02-brain/`（Chat, Context, Query）、`03-hands/`（Shell, Skill, Tools）、`04-guard/`（Security）、`05-memory/`（Conversation, FileIO）、`06-perception/`（CodeIndex, Lsp, Web）、`07-agents/`（Agent, Team）、`08-transport/`（Bridge, Build）、`09-composition/`（Mode, Presentation）

## 组件依赖图（无环分层）

```
L0 叶子（零组件间依赖）:
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
  McpToolHandlers      → Abstractions, Infrastructure
  Scheduling           → Abstractions, Infrastructure
  Agents               → Abstractions, Infrastructure

L6 组合根:
  Composition          → Bridge, Mcp, Brain, Guard, Hands, Eyes, Vault, Scheduling, McpToolHandlers, Agents, Transport.Contracts, Transport.Impl

L7:
  Clock                → Composition, Vault, Scheduling

Host:
  JoinCode (jcc.exe)   → Brain, Hands, Eyes, Vault, Composition, Guard, Clock, Bridge, Dream, Browser, Transport.Contracts, Transport.Impl
```

> 所有组件隐式依赖 `Abstractions` + `Infrastructure`（上表省略以突出组件间关系）

## 组件详情

| 组件 | 路径 | 层 | 职责 | 关键 NuGet | 源码生成器 |
|------|------|----|------|-----------|-----------|
| Transport.Contracts | `08-transport/Contracts/` | L0 | 传输协议契约 | — | Enum, CI |
| Transport.Impl | `08-transport/Impl/` | L0 | 传输实现 | — | CI |
| Llm | `01-ai/Llm/` | L0 | LLM 适配器（OpenAI/Anthropic/Azure/Pipe） | Microsoft.Extensions.DI, Options | Enum, CI |
| CodeIndex | `06-perception/CodeIndex/` | L0 | 代码索引引擎（TreeSitter） | TreeSitter.DotNet | CI |
| Browser | `06-perception/Browser/` | L0 | 浏览器自动化（卫星包） | PuppeteerSharp | CI |
| Bridge | `08-transport/Bridge/` | L1 | 进程桥接服务 | Microsoft.Extensions.Hosting, QRCoder | Enum, CI, CliOption |
| Mcp | `01-ai/Mcp/` | L1 | MCP 协议客户端 | ModelContextProtocol, Microsoft.Extensions.DI | Enum, CI |
| Dream | `01-ai/Dream/` | L1 | 记忆整合插件 | Microsoft.Extensions.Hosting | Enum, CI, CliOption, McpTool |
| Guard | `04-guard/Guard/` | L2 | 权限/安全/Hook/OAuth | Microsoft.Extensions.Http, TreeSitter.DotNet | Enum, CI |
| Eyes | `06-perception/Eyes/` | L2 | 代码索引服务/LSP | Microsoft.Extensions.Hosting | Enum, CI |
| Vault | `05-memory/Vault/` | L2 | 记忆目录/状态/待办/通知 | Microsoft.Data.Sqlite, SQLitePCLRaw | CI |
| Brain | `02-brain/Brain/` | L3 | 查询引擎/上下文/提示词/计划/成本 | Microsoft.Extensions.Options | Enum, CI, PromptSection |
| Hands | `03-hands/Hands/` | L4 | 工具执行/Shell/Web/Notebook/API/缓存 | ImageSharp, Docnet.Core, ReverseMarkdown.Aot | CI |
| McpToolHandlers | `03-hands/McpToolHandlers/` | L5 | MCP 工具处理器 | ModelContextProtocol | McpTool, Enum, CI |
| Scheduling | `03-hands/Scheduling/` | L5 | 任务调度/Cron/持久化 | Microsoft.Extensions.DI | Enum, CI |
| Agents | `07-agents/Agents/` | L5 | Agent 协调/生命周期/Fork/Team | Microsoft.Extensions.Caching.Memory | McpTool, Enum, CI |
| Composition | `09-composition/Composition/` | L6 | 依赖注入集成层（组合根） | ModelContextProtocol | Enum, CI, McpTool |
| Clock | `09-composition/Clock/` | L7 | 目标引擎/工作流宿主 | Microsoft.Extensions.Logging | CI |

> **Enum** = EnumMetadata.Generator, **CI** = ConstructorInjection.Generator, **McpTool** = McpToolHandlers.Generator, **PromptSection** = PromptSection.Generator, **CliOption** = CliOption.Generator

## 组件内部结构

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

## 源码生成器

| 生成器 | 路径 | 用途 | 使用范围 |
|--------|------|------|---------|
| AotSafety.Generator | `generators/AotSafety.Generator/` | AOT 安全分析器 | 全局（根 Directory.Build.props） |
| JccCodeFixes | `generators/JccCodeFixes/` | JCC 代码修复 | 全局（根 Directory.Build.props） |
| EnumMetadata.Generator | `generators/EnumMetadata.Generator/` | 枚举元数据（[EnumValue] → XxxConstants + XxxExtensions） | 几乎所有组件 |
| ConstructorInjection.Generator | `generators/ConstructorInjection.Generator/` | 构造函数注入（[Register] → DI 注册代码） | 几乎所有组件 |
| McpToolHandlers.Generator | `generators/McpToolHandlers.Generator/` | MCP 工具处理器注册 | McpToolHandlers, Agents, Composition, Dream, JoinCode |
| PromptSection.Generator | `generators/PromptSection.Generator/` | 提示词段落生成 | Brain |
| CliOption.Generator | `generators/CliOption.Generator/` | CLI 选项绑定 | Bridge, Dream, JoinCode |
| AppModule.Generator | `generators/AppModule.Generator/` | 应用模块注册 | JoinCode |
| MiddlewareOrder.Generator | `generators/MiddlewareOrder.Generator/` | 中间件顺序验证 | — |

## Host 项目 (`src/JoinCode/`)

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

## 测试结构

```
tests/
├── Unit/
│   ├── Host.Tests/              Host 单元测试
│   ├── Infra.Tests/             Infrastructure 单元测试
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
│   └── Sync.Integration.Tests/  同步集成测试
└── Benchmarks/
    └── Eyes.Benchmarks/         性能基准
```

**组件测试**：每个组件有 `tests/Unit/` 子目录，如 `components/01-ai/Mcp/tests/Unit/Mcp.Tests.csproj`

## SDK 聚合包 (`sdk/Sdk/`)

一行代码引用所有组件：`JoinCode.Sdk` 引用 Abstractions + Infrastructure + 全部 14 个组件

## 中间件管道清单

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

## 关键配置文件

| 文件 | 路径 | 说明 |
|------|------|------|
| 根 Directory.Build.props | `Directory.Build.props` | 全局：net10.0, AOT, 版本变量, 全局 Analyzer |
| 根 Directory.Build.targets | `Directory.Build.targets` | IsPackable/GenerateDocumentationFile 条件逻辑 |
| components Directory.Build.props | `components/Directory.Build.props` | 组件：测试框架 |
| generators Directory.Build.props | `generators/Directory.Build.props` | 生成器：netstandard2.0 |
| sdk Directory.Build.props | `sdk/Directory.Build.props` | SDK：全局 Using |
| src Directory.Build.props | `src/Directory.Build.props` | 源码：全局 Using |
| tests Directory.Build.props | `tests/Directory.Build.props` | 测试：xUnit, Moq, FluentAssertions |
| global.json | `global.json` | .NET SDK 10.0.301 |
| nuget.config | `nuget.config` | NuGet 源 + 本地包目录 |
| src/GlobalUsings.cs | `src/GlobalUsings.cs` | Host 全局 Using |
| Abstractions/GlobalUsings.cs | `sdk/Abstractions/GlobalUsings.cs` | 73 行全局 Using |
| Infrastructure/GlobalUsings.cs | `src/Infrastructure/GlobalUsings.cs` | 73 行全局 Using |

## 构建命令速查

```powershell
# 单组件快速编译
.\build.ps1 -Fast -SkipTests -Component Mcp

# 单组件单元测试
dotnet test "components/01-ai/Mcp/tests/Unit/Mcp.Tests.csproj" -c Debug --filter "Category!=Integration"

# 全量编译+测试（提交前）
.\build.ps1 -Fast

# 仅生成器
.\build.ps1 -Fast -SkipTests -GeneratorsOnly

# 仅组件（不编译 Host）
.\build.ps1 -Fast -SkipTests -ComponentsOnly
```

## 组件名→路径映射

| 组件名 | 路径 |
|--------|------|
| Llm | `components/01-ai/Llm/` |
| Mcp | `components/01-ai/Mcp/` |
| Dream | `components/01-ai/Dream/` |
| Brain | `components/02-brain/Brain/` |
| Hands | `components/03-hands/Hands/` |
| McpToolHandlers | `components/03-hands/McpToolHandlers/` |
| Scheduling | `components/03-hands/Scheduling/` |
| Guard | `components/04-guard/Guard/` |
| Vault | `components/05-memory/Vault/` |
| Eyes | `components/06-perception/Eyes/` |
| CodeIndex | `components/06-perception/CodeIndex/` |
| Browser | `components/06-perception/Browser/` |
| Agents | `components/07-agents/Agents/` |
| Bridge | `components/08-transport/Bridge/` |
| Transport.Contracts | `components/08-transport/Contracts/` |
| Transport.Impl | `components/08-transport/Impl/` |
| Composition | `components/09-composition/Composition/` |
| Clock | `components/09-composition/Clock/` |
