# TASK127 — 查询期自动记忆召回（确定性打分，零 LLM）

> 状态：**已施工** · 日期 2026-11-23 · 总账编号：TASK103 §2.3 **#42 / TASK071-C2**（承 [TASK109](TASK109-召回算法差异-判据取证.md) 裁决「显式检索维持；自动召回另立专项」）
> 基准树：`D:\project\claude-code-rust\claude-code-rev-main\src` · 本仓库：`D:\project\w2`

---

## 0. 结论（先看这里）

TASK109 已证：原文「关键词打分 vs LLM sideQuery」**比较的不是一个动作**（显式检索 vs 每回合自动召回），判据不适用。真缺口是**自动查询期召回缺失**。

本轮补的是**流水线**，不换算法：

- 用本仓现成的 `IMemoryManagementService.ScanMemoriesAsync(query, limit)` —— 内部已是「文件扫描 → `IMemoryRelevanceSelector` 打分 → `IMemoryTruncator` 截断」，**零 LLM、零新增算法**；
- 落点 = `preprocess-prepare-pipeline` 新增 `memory-recall-mw`，把结果写进**动态系统消息**；
- sideQuery 版留作后续**可替换实现**（`IMemoryRelevanceSelector` 是接口，换 DI 即可，中间件不用改）。

**为什么先做确定性版**：sideQuery 是"每回合一次 Sonnet"，属 TASK107 明令"无人值守不做"的产品权衡（延迟 vs 成本 vs 记忆新鲜度）。确定性打分不引入新成本，只把已有能力接到缺失的流水线上——这是补断链，不是新设计。

---

## 1. 四问回答（TASK109 §3 遗留）

### 问1 副作用边界：是否污染会话历史

**不污染**。落点写的是 `IChatContextManager.AddDynamicSystemMessageAsync`，而动态系统消息在 `SystemPromptMiddleware` 每回合 `ClearDynamicSystemMessagesAsync` 后重建（`SystemPromptMiddleware.cs:51-59`）：

- 不进 `MessageList` 会话历史 → 下一回合不会作为对话内容回放；
- 不动 `StaticPrefix` → 本仓已有 `CacheBreakMonitor` 专门监控这类断裂，动态段是刻意留出的可变区。

### 问2 触发门

| 门 | 规则 | 理由 |
|---|---|---|
| 开关 | `WorkflowConfig.MemoryAutoRecallEnabled`，**默认 true** | 零 LLM 成本，默认开没有烧钱风险；关掉只需一个配置键 |
| 命令排除 | `Message` 以 `/` 开头 → 不召回 | 斜杠命令不是自然语言查询，打分必然噪声 |
| 最小长度 | `Message.Trim().Length >= 4` | 基准用「含空格」（英文词边界）；中文没有空格，改用字符长度 |
| 记忆非空 | `TotalMemories > 0` | 空库直接跳过，省一次扫描 |
| 分数门 | 最高分 `< 0.15` → 不注入 | 全不相关时宁可不注入，避免"噪声记忆"长期占上下文 |

### 问3 成本

- **token**：0（无 LLM 调用）；
- **上下文**：`MaxMemories = 5`、`MaxTotalChars = 1500`，单条 `Content` 截断 240 字符；
- **IO**：每回合一次记忆目录扫描（`ScanAllAsync` 读已加载的索引，非全盘 rglob）。

### 问4 记忆模型差异：结构化 `MemoryEntry` 如何呈现

本仓 `MemoryEntry`（`memdir2/core/MemoryEntry.cs`）有 `Id/Type/Content/Title/Tags/CreatedAt/AccessCount`，基准是 Markdown 文件头 + `mtime`。映射方式：

- 标题优先 `Title`，缺失时取 `Content` 首行前 40 字符；
- 描述取 `Content` 前 240 字符（`MemoryTruncator` 已在扫描链里做过一轮）；
- **不透传 mtime 新鲜度提示**：本仓有 `MemoryFreshnessNote`，但自动召回每回合都跑，逐条标"X 天前"会让稳定记忆反复抖动，记入偏差 §4。

---

## 2. 关键设计：为什么直接复用 `ScanMemoriesAsync`

一度考虑过「`MemoryStore.Search` + 自己格式化」，被否掉，两个原因：

1. **`MemoryStore.Search` 有写副作用**（`MemoryStore.cs:83-89`：命中后 `WithAccessed` 更新 `AccessCount` 并触发落盘）。每回合自动调用会把访问频率刷爆，**反过来破坏 `/memory search` 的排序信号**——自动召回污染显式检索，这是不可接受的耦合。
2. `MemoryStore` 在生产**没有任何消费点**（全仓 grep 只剩本地化字符串），真正的记忆源是 `MemoryManagementService` 的 `MemoryScanner`（扫 `memdir` 下的记忆文件）。走 `ScanMemoriesAsync` 才是生产同一份数据。

`ScanMemoriesAsync` 走 `_mgmtActor` 串行化，`MemoryScanner.ScanAllAsync` 只读索引，不更新访问统计。

---

## 3. 实现落点

| 文件 | 改动 |
|---|---|
| `modules/cognition/brain.context_services/preprocess/MemoryRecallMiddleware.cs` | 新建。`IPreparePreprocessMiddleware`，`[Register]` |
| `modules/cognition/brain.context_services/Brain.ContextServices.csproj` | 加 `Vault.csproj` 引用（Vault 只依赖 abstractions，无环） |
| `modules/composition/pipelines/registration/DataDrivenPipelineComposition.cs` | `BuildLocalNodeFactories()` 加 `preprocess-prepare-pipeline:memory-recall-mw` |
| `resources/profiles/base.yaml` | `preprocess-prepare-pipeline` 加节点 `priority: 150`（紧跟 `system-prompt-mw:100`，必须在它之后——清空动态消息在它里面） |
| 配置键 `memoryAutoRecallEnabled` | 按 `ConfigKeyRegistry` 唯一清单加：`ConfigKeys` 常量、`ConfigKeyRegistry` 条目、`WorkflowConfig` 属性、`CurrentSettings`（`SettingsJson.cs`）属性、`EngineApplicationBuilder` 三处映射 |

**顺序为什么是 150**：`system-prompt-mw:100` 会先 `ClearDynamicSystemMessagesAsync`，再写自己的 `DynamicSuffix`。召回必须在它之后追加，否则被清掉。

**失败降级**：全 `try/catch`，任何异常（扫描失败、取消、格式化失败）都只 `Diag.WriteLine` 后放行 `next`，**不注入、不抛、不影响回合**。

---

## 4. 记录在案的偏差

1. **无会话级曝光去重**（基准 `alreadySurfaced` + 曝光字节预算）：确定性 top-5 在输入不变时稳定，重复注入等价于"常驻提示"，成本上限 1.5KB/回合；基准的去重是为 LLM 选择的高方差兜底，本仓无此问题。若将来换 sideQuery，必须同时补去重。
2. **不透传 mtime 新鲜度**：见 §1 问4。
3. **不做子代理隔离**：基准召回限定主会话；本仓 `PreprocessContext` 无 agent 标识（与 TASK126 §4 同一处缺失）。影响被「预算 1.5KB」压到可忽略。
4. **中文触发门用字符长度而非空格**：见 §1 问2。

---

## 5. 验证

### 5.1 单测

| 用例 | 断言 |
|---|---|
| 开关关闭 | 不调 `ScanMemoriesAsync`，不写动态消息 |
| 斜杠命令 | 不召回 |
| 过短输入 | 不召回 |
| 空库 | 不召回 |
| 最高分低于阈值 | 不召回 |
| 正常命中 | 写一条动态消息，含标题与内容摘要 |
| 超预算 | 条数 ≤ 5、总字符 ≤ 1500 |
| 扫描抛异常 | 不抛、不写、`next` 仍被调用 |

### 5.2 装配验证

`--dump-nodes` 实测：**254 节点**，`preprocess-prepare-pipeline` 从 5 节点变 **6 节点**，`memory-recall-mw (priority=150)` 在 base / ci / headless / sdk **四个 profile 全部生效**（顺序：crash 50 → system-prompt 100 → **memory-recall 150** → reminder 200 → tool-listing 300 → lsp 400）。

`Verify-NodeFactoryCoverage.py`：254 节点 / 254 工厂键，`[OK] 每个声明节点都有工厂来源，且实例型 / 声明型与来源一一对应`（middleware 实例型 179）。

顺序类门禁（`timeout 30` 串行）：

| 门禁 | 实测 | 结论 |
|---|---|---|
| `Verify-ExplicitOrder.py` | **12.0s**，exit=0 | `jcc prompt` 段落 49/68、`jcc --debuglog -p` 模块 9/9 |
| `Verify-RegistrationOrder.py` | **11.3s**，exit=0 | 5 列表 / 17 项双向一致 |

### 5.3 真机边界（如实记录）

本机 7 个 vendor 的 `apiKey` 全为空（同 TASK126 §5.3），`jcc -p hi` 在 **12.4s** 后以 `HTTP 401: Provider 'sensenova' API Key 未配置` 结束，`exit=10`。模块启动阶段（4.3s）已打完 `[MODULE] ... start` 全部 9 行，因此**模块顺序是真机证据**；但回合级的 `[MEM-RECALL]` 打点本轮看不到。

验证止于：

- 单测 **11/11**（`brain.context.tests` 全量 **954/954**）；
- 装配：`--dump-nodes` + 工厂门禁 + `IChatPreprocessor` 按 profile 解析（解析数≠声明数 fail-fast，TASK100 S9 的结构性保证）。

配好 key 后补：`JCC_DEBUGLOG=1 jcc -p "..."` 看 `[MEM-RECALL] inject chars=` / `skip: <门名>`。

### 5.4 施工中撞到的两个门禁缺陷（已修）

1. **`Verify-RegistrationOrder.py` 的测试路径漏排**：只挡 `/tests/`，挡不住本仓 `xxx.tests/`，测试替身被当生产实现。已补 `.tests/`。
2. **`Verify-ExplicitOrder.py` 的子进程上限是 240s**：`-p hi` 在无 key 时会重试，外层 `timeout 30` 只能硬杀且丢输出。已降到 **25s**，且超时保留部分输出继续比对（`[MODULE]` 行在启动阶段就打完了）。

> 附带证伪一条上一轮我自己写进规范的结论：「互不依赖的脚本并行跑，墙钟取 Max」。实测两个门禁单独跑 12.0s + 11.3s，**并行时 `Verify-ExplicitOrder` 超 30s 被杀**——两个 jcc 进程抢 CPU 与文件缓存。已在 `AGENTS.md` 改为「带子进程的脚本串行跑」。
