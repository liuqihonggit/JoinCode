# ADR 0150: 记忆子系统分层保留 — SessionMemory 对齐 TS + vault.memdir 显式触发

> **状态**：accepted
> **日期**：2026-10-26
> **关联**：TASK071-B2 记忆子系统架构分歧

## 上下文

C# 侧有两套记忆子系统：

1. **系统 A：SessionMemory**（`modules/cognition/brain/context/compact/services/core/`）
   - 对齐 TS 基准 `src/memdir/`，使用 Markdown 文件存储
   - 无 TTL、无归档、无自动删除
   - 与基准侧设计哲学一致：记忆永不过期，由 LLM/用户手动维护

2. **系统 B：vault.memdir**（`modules/storage/vault/memdir/`）
   - C# 独有扩展，TS 基准无对应实现
   - 结构化存储（`MemoryEntry` JSON），带 TTL/AccessCount/RelevanceScore
   - 有自动删除/归档/衰减逻辑（90 天归档、180 天删除、指数衰减）

基准侧（TS）设计哲学：记忆是 Markdown 文件，永不过期，截断只发生在加载到上下文时（`MAX_MEMORY_FILES=200`），文件本身不删。删除只由 LLM/用户手动决定。

## 决策

采用**方案 C 分层保留**：

- **SessionMemory（系统 A）**：保持对齐 TS 不动 — Markdown 存储、无自动删除
- **vault.memdir（系统 B）**：保留自动删除/归档能力，但**默认不自动调用** — 只在用户显式触发时执行（`/memory cleanup` 命令或 `memory_cleanup` MCP 工具）

## 理由

1. **系统 A 已对齐 TS** — SessionMemory 使用 Markdown 存储、无删除，与基准侧设计哲学一致，不需要改动
2. **系统 B 是 C# 独有扩展** — vault.memdir 的结构化存储 + TTL + 衰减是 C# 面向长期运行服务场景的有意扩展，TS 侧没有对应概念
3. **系统 B 已是显式触发** — `CleanupOldMemoriesAsync` 只在 `/memory cleanup` 命令和 `memory_cleanup` MCP 工具中调用，无任何自动/定时调用，已是"默认不删除"状态
4. **数据安全** — 用户不显式调用清理，记忆不会被删除，与 TS"永不过期"语义一致

## 验证

- `CleanupOldMemoriesAsync` 调用点：仅 `MemoryCommand.cs:463`（`/memory cleanup` 命令）和 `MemoryManagementToolHandlers.cs:128`（`memory_cleanup` MCP 工具）
- 无任何后台定时器、启动时自动清理、PeriodicTimer 等自动调用机制
- 测试锁定：`MemoryManagementServiceCleanupGuardTests` 验证清理参数校验

## 替代方案（考虑过但放弃）

- **方案 A（严格对齐 TS）**：移除 vault.memdir 的自动删除/归档。放弃原因：丢失 C# 独有的结构化记忆管理能力
- **方案 B（保留 C# 扩展 + 补 ADR）**：保留自动删除但不改默认行为。放弃原因：虽然已是显式触发，但缺少 ADR 记录设计意图，方案 C 补齐了 ADR
