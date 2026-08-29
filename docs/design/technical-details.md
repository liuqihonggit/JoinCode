# 技术要点

> 此文档从 README 摘出，详细描述 JoinCode 的核心技术实现。

## 1. 宽容处理

引入了 CommandCode 作者针对 DeepSeek 工具调用的容错方案，通过 `ToolCallRepairService` 实现多层容错机制，降低 LLM 工具调用出错概率：

### 1.1 JSON 格式修复（RepairJson）

自动修复 LLM 返回的常见 JSON 格式问题：

| 问题类型 | 修复方式 | 示例 |
|----------|----------|------|
| 尾随逗号 | 移除对象/数组末尾多余逗号 | `{"a":1,}` → `{"a":1}` |
| 未加引号的键 | 自动添加双引号 | `{name:"test"}` → `{"name":"test"}` |
| 单引号键 | 转换为双引号 | `{'key':'value'}` → `{"key":"value"}` |
| 截断的 JSON | 自动闭合未关闭的字符串和括号 | `{"a":"test` → `{"a":"test"}` |

### 1.2 参数名归一化（RepairArguments）

处理 LLM 返回的参数名与工具 Schema 不匹配的情况：

- **大小写不敏感匹配**：`FilePath` → `file_path`
- **别名映射**：`path` → `file_path`，`cmd` → `command`
- **snake_case/camelCase 自动转换**：`fileName` → `file_name`
- **优先级**：直接匹配 > 别名匹配 > 大小写匹配 > 格式转换

### 1.3 参数类型自动转换（RepairArgumentTypes）

根据工具 Schema 的类型定义，自动转换参数类型：

- **字符串 → 整数**：`"42"` → `42`
- **字符串 → 数字**：`"3.14"` → `3.14`
- **字符串 → 布尔值**：`"true"` → `true`
- **字符串 → 数组**：`"[1,2,3]"` → `[1,2,3]`
- **数组 → 字符串**：`["text"]` → `"text"`

### 1.4 工具名归一化（RepairToolName）

将 LLM 返回的任意大小写工具名归一化为标准名：

- 利用各工具名枚举的 `FromValue`（OrdinalIgnoreCase）反查
- 支持所有内置工具的大小写不敏感匹配
- 找不到匹配则返回原名（可能是 MCP 工具或自定义工具）

### 1.5 LLM 结构化输出统一门控（LlmJsonHelper）

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

// 工具调用 JSON! JSON 修复
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

## 2. 前缀缓存策略

对齐 DeepSeek-Reasonix 的部分亮点，通过多层机制确保前缀缓存命中，降低 token 消耗成本：

### 2.1 系统提示词分区（SystemPromptBuilder）

将系统提示词分为静态前缀和动态后缀：

- **静态前缀**：会话期间保持不变的内容（如工具定义、核心指令），确保前缀缓存命中
- **动态后缀**：每轮可能变化的内容（如当前时间、会话状态），不影响静态前缀的缓存
- **分区构建**：通过 `BuildPartitioned()` 方法自动分离，标记 `CacheBreak=true` 的 section 进入动态后缀

### 2.2 消息历史前缀保持

确保消息操作不破坏前缀缓存：

- **撤回操作**（`/rewind`）：移除尾部消息后，剩余消息必须是原始消息的前缀
- **追加日志**（AppendOnlyLog）：所有消息变更都保证前缀特性，避免缓存失效
- **自动压缩保护**：缓存命中时（`CacheReadInputTokens>0`）在 soft threshold（50%）~ 硬阈值（80%）之间推迟折叠（`Deferred`），达 `DeferFoldLimit` 次或缓存变冷才真正压缩，保护前缀缓存（对齐 Reasonix Go 版分层折叠）

### 2.3 DeepSeek 缓存统计

支持 DeepSeek 特有的缓存统计字段：

- **prompt_cache_hit_tokens**：缓存命中 token 数
- **prompt_cache_miss_tokens**：缓存未命中 token 数
- **时间统计显示**：在 `[Timing]` 行中显示缓存命中情况（如 `缓存=命中120/未命中30`）

### 2.4 设计目标

1. **成本优化**：通过前缀缓存减少重复 token 消耗
2. **会话一致性**：确保消息操作（撤回、压缩）不破坏缓存
3. **可观测性**：提供缓存命中统计，便于成本分析

## 3. 死循环处理策略

### 3.1 检测机制：OutputLoopDetector

基于滑动窗口的重复模式检测器，参数可配置：

- **窗口大小**：2000字符（检测最近2000字符的尾部）
- **模式长度范围**：10-500字符
- **重复次数阈值**：10次（同一模式连续出现10次视为循环）
- **检查间隔**：每50字符检查一次
- **冷却期**：500字符（检测到循环后暂停检测，避免频繁触发）

检测算法：从最大模式长度向最小模式长度遍历，检查文本尾部是否存在连续重复的模式。一旦检测到重复次数≥阈值，立即触发干预。

### 3.2 干预机制：三级漏斗策略

通过 `LoopInterventionMiddleware` 实现渐进式干预：

| 级别 | 触发条件 | 干预动作 | 恢复策略 |
|------|----------|----------|----------|
| **Level 1** | 第1~2次检测到循环 | 软干预：注入提示词，流继续 | - |
| **Level 2** | 第3~4次检测到循环 | 硬截断：撤回本轮对话 + 降低温度(0.6) + 重新发起LLM调用（最多2次重试） | 重试成功则继续；重试失败则升级到Level 3 |
| **Level 3** | 第5次+或重连失败 | 上下文压缩：自动压缩对话历史，保留最近1轮用户消息作为种子，无人值守恢复 | 压缩成功则继续；失败则重置到起点 |

### 3.3 智能推进折扣

通过 `ITaskProgressTracker` 监控任务进度（如TODO完成情况），如果检测到循环期间任务有实际推进，则有效触发次数减少1（`ProgressDiscount`），降低干预级别，避免误伤正常推进的复杂任务。

### 3.4 配置参数

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

### 3.5 设计理念

1. **渐进式干预**：从软提示到硬截断再到上下文压缩，逐步升级
2. **智能恢复**：通过降温和重连尝试打破循环，而非直接放弃
3. **任务感知**：考虑任务推进情况，避免打断正常工作的复杂任务
4. **无人值守**：Level 3压缩后自动恢复，无需用户干预
5. **审计追踪**：Level 2撤回时插入审计标记，便于问题排查

### 3.6 模型层

1. 模型层用切片查看逻辑循环位置，回溯起因，然后微调输出，或通过稀疏自编码器对这部分权重加衰减惩罚。难度高，属于模型厂商工作，通常仅适合高频触发场景。
2. 用简单模型做检测，但部署和运行成本高。好处是拥有数据，投入下次模型训练后可更好地规避此类死循环。

## 4. 并行动态负载

1. 必须改为 LINQ 链式调用。
2. 动态计算当前 CPU 负载并分级：90% 以上用 1 核心，70% 以上用一半核心，其余用全部核心。
3. 使用标准 System.Linq，通过 Directory.Build.props 全局引用。

## 5. 串行编译

为防止多个 SubAgent 同时触发编译，从 bash 层拦截，统一加入 BuildQueue 队列排队执行，避免并行开发时因内存消耗导致卡死。
