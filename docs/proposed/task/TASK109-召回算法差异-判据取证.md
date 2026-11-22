# TASK109 — 召回算法（TASK103 §2.3 #42 取证）

> 状态：**判据差异不适用（不同语义层）；自动查询期召回挂起为专项设计，本轮不做** · 日期 2026-11-22 · 总账编号：TASK103 §2.3 **#42 / TASK071-C2**
> 基准树：`D:\project\claude-code-rust\claude-code-rev-main\src`
> 本仓库：`D:\project\w2`

---

## 0. 结论（先看这里）

原文「召回算法：关键词打分取10 vs LLM sideQuery 选≤5（含 recentTools/已曝光过滤）」**两半都属实，但比较的不是一个动作**：

- 基准 `findRelevantMemories`（`src/memdir/findRelevantMemories.ts:39-141`）是**每回合自动召回**：扫描记忆文件头 → Sonnet `sideQuery` 结构化选 ≤5 个文件名 → 生成 `relevant_memories` 附件注入会话。调用点只有 `utils/attachments.ts:2217`（`getRelevantMemoryAttachments`），由 `startRelevantMemoryPrefetch:2392` 在用户输入多词且未超本会话曝光字节预算时异步预取；过滤 `recentTools`、`alreadySurfaced`、`readFileState`，并透传 `mtimeMs`。**基准 `/memory` 命令不调用它**（`src/commands/memory/memory.tsx` 是 CLAUDE.md 管理）。
- 本仓 `MemoryRelevanceSelector`（`modules/storage/vault/memdir/memdir2/search/MemoryRelevanceSelector.cs:56-119`）是**显式检索的评价函数**：关键词交集 0.4 + 标签 0.15 + 标题 0.2，乘类型权重/老化/访问频率，`Take(maxResults=10)`。消费点是 `/memory search`（`composition/slash/agents/agent/MemoryCommand.cs:328/360`）与 `memory_search` 工具（`MemoryManagementToolHandlers.cs:33`），**都是用户主动发起**。
- 因此「把关键词打分换成 sideQuery」没有落点：本仓没有回合级自动记忆附件这条流水线；换掉显式检索的评价函数不会带来基准的自动召回，只会给 `/memory search` 增加一次 LLM 调用并丢掉结构化评分。真正的差距是**自动查询期召回缺失**（TASK071 服务域已单列），属新行为：每回合一次 Sonnet 调用 + 新的附件通道 + 曝光去重/字节预算/取消语义，与 #40 dream 同类。

**裁决**：显式召回维持本仓关键词打分（功能目标不同，不是退化）；自动查询期召回**登记专项设计，本轮不做**。

---

## 1. 四问回答

### 问1 专家隐性实践
"记忆召回"有两种成熟形态，不能混谈：①**系统提示词常驻索引**（MEMORY.md 全量或摘要）稳定但占固定上下文；②**按查询相关的按需附件**（基准形态）省上下文但要付出一次 sideQuery 和去重状态。显式 `search` 工具是第三种，由用户决定何时检索，适合可预期的信息查找。三者可共存，基准就是 ①+②，本仓是 ①+③。

### 问2 原始素材（一手）
- 基准：`src/memdir/findRelevantMemories.ts:18-24`（选择提示词："up to 5"、recentTools 排除 API 文档但保留警告/gotcha）、`:39-51`（`alreadySurfaced` 预过滤）、`:77-130`（`sideQuery` + `getDefaultSonnetModel` + `json_schema` + `selected_memories`，`max_tokens:256`，`querySource:'memdir_relevance'`）、`:131-140`（失败/取消均返回空，不阻塞回合）；`src/utils/attachments.ts:2196-2241`（目录选择支持 @agent memory 隔离、双过滤后 `.slice(0,5)`）、`:2372-2400`（触发前置：最后一条非 meta 用户消息、必须含空格、会话曝光字节预算、turn 级 abort 子控制器）。
- 本仓：`modules/storage/vault/memdir/memdir2/search/MemoryRelevanceSelector.cs:20/59`（默认 `maxResults=10`）、`:62-71`（AhoCorasick 过滤归档/过期并按分排序）、`:85-119`（六段评分公式）；`modules/storage/vault/memdir/services/MemoryManagementService.cs:364-389/568`（扫描/增删走同一个 selector）；`modules/composition/slash/agents/agent/MemoryCommand.cs:290/328/360`（`/memory` 的 remember/search/list 三个分支，search 才是召回消费点）；`modules/cognition/prompts/sections/memory/MemorySection.cs:88-110`（系统提示词加载 `MEMORY.md` 全文，无相关性筛选）。
- 架构差异佐证：本仓记忆是结构化 `MemoryEntry`（Title/Tags/Type/AccessCount/归档过期），基准是 Markdown 文件头清单（`formatMemoryManifest`），提示词与选择协议无法直接互换。

### 问3 具体方法与工具
两侧一手源码逐行对照；全仓 grep `findRelevantMemories` 基准只有附件预取一个生产调用点，`/memory` 命令目录不引用；本仓 grep `SelectRelevantMemoriesAsync` 生产只到 `MemoryManagementService`（服务接口）与显式命令/工具，再向 `QueryEngine`/`QueryLoopMiddleware` 查无自动附件注入点。

### 问4 独立验证来源
基准 `findRelevantMemories.ts:35-38` 注释明写 "query-time recall"，`attachments.ts:873` 注释 `relevant_memories moved to async prefetch` 互证其自动性；本仓 `MemorySection` 的"何时访问记忆"提示词让模型**自行调用工具**，说明设计上把召回决定权留给模型/用户，而非系统每回合预取。

---

## 2. 差异与判定

| 维度 | 基准自动召回 | 本仓显式检索 | 判定 |
|---|---|---|---|
| 触发 | 每回合异步预取（多词用户输入） | 用户 `/memory search` 或 `memory_search` | 不同层 |
| 算法 | Sonnet sideQuery 结构化选 ≤5 文件名 | 关键词/标签/标题/类型/老化/访问频率打分取 10 | 各符其层，无优劣可比 |
| 过滤 | recentTools / alreadySurfaced / readFileState / 曝光字节预算 | 归档/过期过滤 + 分数 >0 | 基准多的是**会话曝光状态**，本仓无此概念 |
| 输出 | 附件（带 mtime 的新鲜度提示） | 文本列表（`/memory search` 逐条渲染） | 消费端不同 |
| 失败 | 吞异常回空列表，不阻塞回合 | 同步计算无 LLM 失败面 | 本仓更稳 |
| token 成本 | 每回合一次 Sonnet（256 max tokens） | 0 | 新增行为的成本权衡点 |

**不裁决为"本仓缺陷"**：原文把两种形态放在一行比较，容易读成"本仓少了个更好的算法"，实际是本仓**没有基准那条自动召回流水线**。若只把显式检索改成 sideQuery，是给低频用户命令加 LLM 依赖，没有对齐自动召回。

---

## 3. 裁决与后续启动条件

1. **显式召回**：保持关键词打分与 `maxResults=10`。它有确定性与零延迟优势，且服务 `/memory search`、`memory_add` 去重等既有逻辑；无证据显示误召回造成伤害。
2. **自动查询期召回**：与 #40 同类，是新行为（每回合 LLM + 附件通道 + 会话状态），无人值守不引入。另立专项，启动前需回答四问：
   - 副作用边界：是否用 sideQuery（本仓已有 `PermissionExplainerService` 的旁路调用范式），绝不污染会话历史；
   - 触发门：最小输入长度、每会话曝光文件/字节上限、已读取文件过滤如何映射到本仓 `IFileSystem`/附件模型；
   - 模型成本：默认开关、模型档位、超时与失败降级（必须不阻塞回合）；
   - 记忆模型差异：本仓结构化 `MemoryEntry` 如何向选择器呈现"文件名+描述"清单，以及 how to surface mtime/新鲜度。
3. **总账登记**：#42 保留未划（真实待办，非不适用），状态写明"判据不适用 + 自动召回专项"。

---

## 4. 验收（取证项，无代码改动）

- 基准调用点唯一性：`grep findRelevantMemories` 生产命中 `attachments.ts:2217` 一处，命令目录零命中。
- 本仓消费点：`grep SelectRelevantMemoriesAsync` 生产命中 `MemoryManagementService` + `MemoryCommand`/`MemoryManagementToolHandlers`，`QueryEngine`/`QueryLoopMiddleware` 零命中。
- 总账 #42 指向本文档并保留 🟡。
