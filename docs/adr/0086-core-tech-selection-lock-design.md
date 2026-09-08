# 0086. 核心技术选型与锁设计

- 状态：accepted
- 日期：2026-09-08
- 决策者：项目架构组

## 背景

项目使用中间件管道、MCP HTTP 服务端、上下文压缩、AsyncLock 互斥锁等核心技术。锁设计与死锁防护需要统一规范，避免同步锁与异步锁混用导致的死锁问题。

## 详细内容

### 核心技术选型

| 技术 | 用途 | 说明 |
|------|------|------|
| **System.Linq** | LINQ | 标准库，通过 `Directory.Build.props` 全局 `using System.Linq`，所有源码项目自动引用 |
| **MiddlewarePipeline\<TContext\>** | Task 管道 | `Infrastructure.Pipeline` — DI 注入中间件集合，支持 PreHook/PostHook、异常捕获/传播两种模式 |
| **StreamMiddlewarePipeline\<TContext, TEvent\>** | 流式管道 | 同上，返回 `IAsyncEnumerable<TEvent>`，流式场景异常默认传播 |
| **McpHttpServer** | MCP Streamable HTTP 服务端 | `services/Mcp/src/McpProtocol/McpHttpServer.cs` — HttpListener 实现，无状态（不分配 Session-Id）/有状态（分配+DELETE 终止）双模式，GET 开 SSE 推送 NotificationReceived |
| **上下文压缩** | 长对话 token 回收 | Compact（对话级管道）+ Compression（内容级策略）+ Collapse（折叠级）三子系统，Microcompact 纯规则优先、LLM 摘要兜底，CompactOutputGuard 守卫降级 > ADR: [0053](0053-context-compaction-layered-mechanism.md) |
| **AsyncLock 互斥锁** | 统一互斥锁原语 + 死锁诊断 | `TryLock()`/`TryLockAsync()` 超时返回 null（不抛重入异常，ThreadId 在 async 下不可靠）；锁内只操作字段，副作用移到锁外；`TrySetResult` 禁止在锁内调用（续体同线程重入锁自等自） > ADR: [0052](0052-asynclock-unified-mutex-file-access.md)、[0060](0060-asynclock-sync-trylock-fireandforget-deadlock.md)（0059 已被 0060 取代） |

### 锁设计与死锁防护

1. **同步锁与异步锁混用** — 高性能线程安全容器用同步锁 + 同步函数，但大量生产代码用异步函数，必然同时出现异步锁和同步锁混用

2. **AsyncLock TryLock 语义** — 异步锁用自定义 `AsyncLock` + 超时释放 + `using` 释放（NET10 风格）。所有都要用 `TryLock` 语义：尝试加锁 → 超时放弃 → 超时重试（指数退避）→ 超时死等，多策略适配业务复杂性

3. **死锁时改用 Actor 模型** — 业务代码复杂度太高时（如两次 `await` 同一个锁），立即改用 Actor 模型（管道 Channel 通讯无锁模型，用封装私人邮箱接收外部投递）。如果顾客-服务员-厨师架构，服务员要做成**双工双管道通讯**，避免积压信息导致管道卡死其它消费，并加入**背压**控制生产者速度，实在处理不了就**死信队列**。学习 Rust 强迫处理任何边缘场景，否则管道会爆炸

4. **搭配状态机** — Actor 模型可搭配状态机（状态查表事件，动作转移），用显式状态枚举 + switch 表达式实现状态转换，不用隐式 `if-else` + 标志变量

## 替代方案

无。AsyncLock + Actor 模型是处理 .NET 异步锁死锁的既定方案。
