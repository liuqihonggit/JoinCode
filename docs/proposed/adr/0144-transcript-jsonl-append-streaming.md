# ADR 0144: 会话转录存储 JSON 数组改 JSONL 纯追加（按需流式读取）

- 状态：accepted（2026-10-21 落地验证：单元 86 + 集成 2 + 真实存量会话迁移验收 OK；写入侧经真实引擎链路产出 transcript.jsonl 确认）
- 日期：2026-10-21
- 决策者：liuqihong（用户指令：会话改 jsonl 按需读取和追加，现在逻辑有问题）

## 背景

### 问题：transcript.json 整存整取

当前 `~/.jcc/sessions/{id}/transcript.json` 是 JSON 数组格式，`TranscriptFileWriter` 的追加路径（`AppendEntryInternalAsync`/`AppendEntriesInternalAsync`）是"读旧数组 → Add → 写回全量"：

- **写入 O(n)**：每轮对话结束（`TranscriptPersistMiddleware` 差量落盘）都要全量读取 + 全量重写文件。大会话（数百条目 × 长内容）每轮提交代价线性增长，且全量重写有写入中途崩溃损坏整个文件的风险。
- **读取 O(全量)**：`ListTranscriptsAsync`（GUI 侧栏/`/resume`/TUI HistorySeeder 高频调用）为了提取 entryCount + 最后一条 preview，全文 `ReadToEndAsync` + 整体反序列化整个数组。
- `GetCustomTitleAsync` 同样全量加载后倒序扫一条元数据。

### 历史演进的两次反向决策（需正面回应）

| Commit | 决策 | 动机 | 本次重新评估 |
|--------|------|------|-------------|
| #126（9a8bd2172） | JSONL → JSON 数组 | 人类可读（WriteIndented）+ 层次化目录 + 可读会话 ID | 目录组织与会话 ID **已保留且不变**；"人类可读"降级诉求（每行一条紧凑 JSON 仍可读）不足以抵消 O(n) 写入 |
| #163（b9b2ca711） | .jsonl 扩展名统一为 .json | 扩展名统一 | 恢复 `.jsonl` 名实相符；#163 的 55 处替换是格式随扩展名的联动，本次格式真的变了，名不符实（.json 文件装 JSONL 行）反而误导工具与人 |

### JSONL 时代的两个已记录坑（规避方案现成）

- FileMode.Append 文件不存在抛异常（PhysicalTests 考古）→ 用 `FileMode.OpenOrCreate + Seek(End)`
- 跨进程读-写冲突（#74 集群 E2E）→ 沿用 `FileShare.ReadWrite` 流读写（git show a924418df 版实现）

## 决策

**transcript 存储从 JSON 数组整存整取改为 JSONL 每行一条纯追加 + 按需流式读取。**

1. **写入 O(1) 纯追加**：`FileMode.OpenOrCreate + Seek(End)` 批量写行（`RelaxedJsonSerializer.Serialize(entry, TranscriptJsonContext.Default.TranscriptEntry)` 紧凑单行 + '\n'）；Actor 邮箱串行化保留；`MaybeOffloadToPasteStore` 卸载逻辑不变（逐条落盘前生效）。
2. **读取逐行流式**：`ReadLinesAsync` 逐行反序列化 + `ResolveFromPasteStore` 还原；`LoadTranscriptAsync` 聚合（接口语义不变）；`ListTranscriptsAsync` 只数行数 + 解析尾行提 preview（不再全文反序列化）。
3. **新文件名 `transcript.jsonl`**；读取兼容回退：`.jsonl` 不存在时读存量 `transcript.json`（数组格式旧逻辑）——存量会话零迁移可用。
4. **迁移幂等不删旧文件**（沿 MigrateLegacyAsync 红线）：启动时把 ①旧扁平 `{id}.json`（JSONL 行）②子目录 `{id}/transcript.json`（数组）统一迁移到 `{id}/transcript.jsonl`；目标存在跳过。
5. **改动封闭**：全部消费方（CLI/GUI/TUI 三端 + ForkCommand/ResumeCommand/子代理）经 `ITranscriptService`/`IAgentTranscriptService` 抽象，接口签名零变更；改动封闭在 `TranscriptFileWriter` + `TranscriptService`（路径/preview/迁移）+ `AgentTranscriptService`（子代理路径）内部。

## 替代方案（考虑过但放弃）

- **保持 JSON 数组 + 内存缓存增量写**：写入仍需全量重写（崩溃窗口），且缓存丢失即丢数据；不解决 ListTranscripts 全量读。
- **SQLite**：引入原生依赖（违反 NativeAOT 约束偏好），杀鸡用牛刀。
- **保持 .json 文件名装 JSONL 内容**：名不符实，误导工具链与后续维护者。

## 影响面

- 约 10 个直接断言磁盘字节格式的测试用例随格式重写（TranscriptServiceTests/TranscriptFileWriterPasteStoreTests）。
- `HousekeepingService` 按 mtime 清理，格式无关，零影响。
- meta.json / usage-facet.json / diag / tool-results 与 transcript.json 零耦合，零影响。
- 集成测试 `TranscriptPersistIntegrationTests` 走 service API，是最接近真实落盘语义的回归防线。
