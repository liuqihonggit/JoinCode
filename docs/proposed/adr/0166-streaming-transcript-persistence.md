# ADR 0166: 流式旁路落盘 —— 打断/崩溃不再丢整段回复（TASK130-B2 阶段 2）

- 状态: accepted（2026-11-25 决策并实现）
- 日期: 2026-11-25
- 决策者: 用户（「继续后续」授权推进 DSG054 阶段 2）
- 关联: [DSG054 §9.3](../design/DSG054-主会话后台化Ctrl+B设计.md) · [ADR 0144](0144-transcript-jsonl-append-streaming.md) · [ADR 0165](0165-everything-is-background-query-identity.md)
- 影响面: `ITranscriptService`（+2 方法）、`TranscriptService`、`TranscriptPersistMiddleware`、测试 fake

## 背景

会话记录（transcript）现在的落盘时机是**回合结束后**：`TranscriptPersistMiddleware` 透传完整事件流，
`next(...)` 结束后才取 `IChatContextManager` 快照差量，把新增消息写进 `transcript.jsonl`
（`TranscriptPersistMiddleware.cs:45-88`）。

因此只要回合没跑完，这一回合已生成的内容就**一个字都不在磁盘上**：

- 用户按 Esc 打断（TUI 会把输入恢复回输入框，但已生成的正文本随进程状态消失）；
- 进程崩溃 / 被强杀（`bridge_serve` 被 Ctrl+C、OOM、断电）。

用户看到过几百字的回复，重开会话却什么都没有 —— 这不是「优雅降级」，是**记录不完整**。

## 决策

### 1. 旁路文件，不改主文件语义

新增 `{sessions}/{sessionId}/streaming.jsonl`，与 `transcript.jsonl` 同目录、同为 JSONL、
同 `TranscriptEntry` 结构（复用 `TranscriptJsonlContext`，零新 DTO）。

每行是**累积全文快照**（不是增量），`Type = "streaming-partial"`，`Role = "assistant"`：

```json
{"sessionId":"s-1","role":"assistant","content":"（当前已生成的全文）","type":"streaming-partial"}
```

选「累积全文」而不是增量拼接：读端只需取**最后一行**即可拿到最全内容，不需要回放合并；
代价是文件膨胀 O(n²)，所以必须节流（见 3），且文件在回合正常结束时删除。

### 2. 契约只加两个方法

```csharp
Task AppendStreamingSnapshotAsync(string sessionId, string content, CancellationToken ct = default);
Task ClearStreamingAsync(string sessionId, CancellationToken ct = default);
```

不暴露路径、不暴露文件概念 —— 路径知识仍只住在 `TranscriptService`（与 `GetTranscriptPath` 同源）。

### 3. 写入节流：200ms 或 512 字符，先到者触发

`TranscriptPersistMiddleware` 在透传事件时累积 `ChatStreamEventType.Content` 的增量；
每满 200ms 或累积增量 ≥512 字符写一次快照。理由：

- 每个 token 一次 `append` 会把流式吞吐拖进磁盘 IO 抖动区，且本项目对热路径敏感（TASK128）；
- 200ms 的粒度意味着崩溃最多丢 200ms 的生成量，而不是整段回复。

### 4. 合并走读端唯一入口 `LoadTranscriptAsync`

`TranscriptService.LoadTranscriptAsync` 读主文件后检查旁路：**旁路文件存在 ⇒ 上一回合没有正常收尾**，
取其中最后一行内容，若主文件末条 assistant 已含相同内容则跳过，否则追加一条
`{Role=assistant, Content=partial, Type="streaming-partial"}`。

这样 `SessionResumeStep`（CLI `--resume`）、`HistorySeeder`（TUI 历史预热）、`ForkCommand`、
`ResumeCommand` **四处读方自动获得恢复**，不需要各改一遍 —— 恢复语义只有一处实现。

### 5. 清理由写方负责，只清「成功落盘」这一条路径

回合正常结束且主 transcript 写入成功 → `ClearStreamingAsync`；异常/取消路径**不清**，
让旁路留在磁盘上等下一次 `LoadTranscriptAsync` 收编。

## 考虑过的替代方案

| 方案 | 放弃原因 |
|------|----------|
| 每条 token 都 append | 流式吞吐被磁盘 IO 打断，收益（200ms → 单 token）与代价不成比例 |
| 增量行 + 读端拼接 | 读端要回放、要处理重复写入，恢复逻辑比「取最后一行」复杂一个量级 |
| 直接往主 `transcript.jsonl` 写 partial | 主文件语义变成「含未完成回合」，摘要计数/搜索/fork 全部要区分 Type，面太大 |
| 崩溃后靠 `IChatContextManager` 内存恢复 | 进程已死，内存不存在 |
| 用 SQLite 记录流式状态 | 引入第二套存储，且读端仍要合并；旁路文件已足够 |

## 后果

- 正面：打断/崩溃最多丢 200ms 生成量；`--resume` / TUI 历史预热 / fork 自动看到残缺但非空的回合。
- 代价：多一次写（每 200ms）+ 一次读目录检查；旁路文件异常残留时会被收编为一条 assistant 消息
  （因此 `Type` 保留 `streaming-partial` 供上层识别与观测）。
- 不做：不做「逐 token」级别的实时落盘（见上表），不做跨会话的 partial 清理定时任务。

## 验收

- `TranscriptServiceStreamingTests`（storage 域）：快照写入后可读、`LoadTranscript` 收编最后一行、
  主文件已完整时不重复追加、`ClearStreaming` 后不再收编。
- `TranscriptPersistMiddlewareTests`（cognition 域）：流式过程中按节流写快照、正常结束清理旁路、
  取消/异常保留旁路。
- 观测打点：`Diag`/`LogDebug` 带 `[STREAM-PERSIST]`，含已生成字符数与节流触发原因。
