# 0055. 系统提示词 section 注入优化空间

- 状态：proposed
- 日期：2026-09-02
- 决策者：项目架构组
- 关联：[0053](docs/adr/0053-context-compaction-layered-mechanism.md)
- 验证：待实现

## 背景

调查发现 jcc（C# 版）系统提示词的 section 数量远超 Claude Code TS 原版，且缺少 TS 原版的若干缓存优化机制，存在明确的优化空间。

### 数量对比

| 维度 | TS 原版 | jcc | 差距 |
|------|---------|-----|------|
| 总 section | ~20 | 60 | 3 倍 |
| 无条件注入（always） | ~19 | 46 | 2.4 倍 |
| 动态/每轮重算（uncached） | 1 | 14 | 14 倍 |
| 按需触发（关键词/模式） | 0 | 14 | jcc 独有 |

jcc 的 46 个 always section 每轮注入约 5,000-6,000 tokens（约 13,200 字），其中 14 个 IsDynamic=true 每轮重算放动态后缀。

### jcc 已优于 TS 的方面（无需改动）

1. **关键词触发注入**（10 个 section）：用户输入匹配关键词时才注入（如"解释"→ExplanatoryStyleSection），TS 没有此机制
2. **模式条件注入**（4 个 section）：AgentMode/CoordinatorMode 只在对应模式激活时注入，TS 是整体替换系统提示词
3. **动态内容未变复用**（`_dynamicCache`）：动态 section 内容未变时复用上次结果，TS 的 `DANGEROUS_uncachedSystemPromptSection` 每轮必重算
4. **源码生成器自动注册**：`[PromptSection]` 特性 + PromptSection.Generator 自动生成注册表，TS 是手动列出

## 决策（提议）

记录 4 项优化空间，按优先级排列。本 ADR 仅记录分析结论，实现留待后续逐项落地。

### P0：缓存破坏检测机制 ✅ 已实现（[0056](0056-cache-break-detection-enhancement.md)）

TS 原版有完整的两阶段缓存破坏检测（`promptCacheBreakDetection.ts`，660+ 行）：
- Phase 1（pre-call）：`recordPromptState` 计算 systemHash/toolsHash/cacheControlHash/perToolHashes 等 12+ 维度哈希
- Phase 2（post-call）：`checkResponseForCacheBreak` 检查 API 响应的 cache tokens，判断缓存是否被破坏并报告原因

jcc 已有 `CacheBreakDetector`（9 维度），并已补齐：
- 双阈值检测（相对 5% + 绝对 2000，修复漏报）
- haiku 模型排除
- TTL 时段区分（5min/1h/服务端路由）
- `NotifyCacheDeletion` 抑制 cached microcompact 误报
- MCP 工具名脱敏（`WithSanitizedNames`）

详见 ADR [0056](0056-cache-break-detection-enhancement.md)。

### P1：三级 cache scope 精细化 + MCP 降级

TS 原版 `splitSysPromptPrefix` 将系统提示词分 4 块，赋予三级 cache scope：
- `global`：跨组织缓存（仅静态前缀，1P only）
- `org`：组织级缓存（3P providers 默认）
- `null`：不缓存（动态内容）

且有 `shouldUseGlobalCacheScope()` 全局开关和 MCP 工具降级逻辑（MCP 存在时 global→org，因 MCP 是用户级配置会破坏 global cache）。

jcc 的 `AnthropicCacheProtocol` 有 `CacheScope.{None,Org,Global}` 枚举，但 Global 使用场景窄，没有 `shouldUseGlobalCacheScope()` 开关和 MCP 降级。

### P2：显式边界标记 + Attribution header 分离

TS 用 `SYSTEM_PROMPT_DYNAMIC_BOUNDARY` 显式标记在系统提示词数组中分区，`splitSysPromptPrefix` 通过查找标记精确切分 4 块。jcc 通过 `CacheBreak` 标志在 section 级分区，没有显式边界标记，分区粒度是 section 级而非 block 级。

TS 还分离 `x-anthropic-billing-header` 和已知 CLI 前缀集合（`CLI_SYSPROMPT_PREFIXES`），赋予不同 cache scope。jcc 没有此分离。

### P3：always section 数量精简

jcc 的 46 个无条件注入 section 远多于 TS 的 ~19 个。可将部分 always section 转为关键词触发或模式条件注入。但需权衡：过度按需注入会导致 LLM 某些轮次看不到规则，行为不稳定。

## 替代方案

1. **不优化，保持现状**：可接受。jcc 已有按需注入和动态内容复用，实际开销可控。但无法诊断缓存 miss 原因，缓存命中率优化缺乏数据支撑。
2. **只做 P0 缓存破坏检测**：最小投入最大收益。有了检测机制后，可量化指导后续 P1/P2 优化是否值得。
3. **全量实现 P0-P3**：投入大，需逐项验证不破坏现有缓存行为。

## 后果

- 正面（若实现）：可诊断缓存命中率；静态前缀可用 global scope 跨组织缓存；MCP 存在时自动降级避免破坏 global cache；always section 数量向 TS 靠拢减少 token 占用
- 负面（若实现）：缓存破坏检测机制增加 660+ 行代码和每轮哈希计算开销；always section 转条件注入可能导致 LLM 行为不稳定
- 中性：本 ADR 仅记录分析，实现前需逐项写子 ADR 明确范围和验证标准
