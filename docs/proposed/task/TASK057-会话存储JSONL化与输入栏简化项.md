# TASK057 — 会话存储 JSONL 化 + TUI 输入栏简化项三连

> 状态：in-progress
> 前置：TASK056（斜杠补全+历史环）已完结验收
> 用户指令（2026-10-21）：①会话改为 jsonl 按需读取和追加（现在逻辑有问题）②推荐功能全部做

## 任务分解（渐进式：每步 编译 → 测试 → commit）

| # | 任务 | 优先级 | 状态 |
|---|------|--------|------|
| 1 | **transcript 存储改 JSONL**（按需读取+纯追加，消除整存整取） | P0 用户指令 | ⬜ |
| 2 | 参数补全（/model xxx Argument 模式） | P1 | ⬜ |
| 3 | 幽灵文本（mid-input /xx 补全） | P2 | ⬜ |
| 4 | Ctrl+R 历史搜索 | P3 | ⬜ |

## 任务1 背景（调研待补充）

- 现状：`~/.jcc/sessions/{id}/transcript.json` 为 **JSON 数组整存整取**——写入是"读旧数组+追加+写回全量"（每次提交 O(n) 全量重写），读取是全量反序列化+逐条 paste-cache 还原（大会话性能灾难）
- 目标：JSONL 每行一条，追加 O(1)，读取按需流式（分块倒序 — TS readLinesReverse 4KB 先例）
- 关键资产：`TranscriptFileWriter`（Actor 串行写）、`AgentTranscriptService`（subagents/agent-*.jsonl 已是 JSONL 先例）、`MigrateLegacyAsync`（历史迁移）
- **历史疑点**：旧扁平 {id}.jsonl 曾被迁移为 JSON 数组——需查 git 历史确认当初迁移动机，避免踩回旧坑（可能是 paste-cache 引用/原子性/损坏容错问题）

<!-- 🤖 Auto Decision: 待调研后补充 -->
