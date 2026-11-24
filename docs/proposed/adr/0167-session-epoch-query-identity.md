# ADR 0167: 会话代次 —— sessionId 纳入查询身份，`/clear` 变成会话翻页（TASK130-B2 阶段 2）

- 状态: accepted（2026-11-25 决策并实现）
- 日期: 2026-11-25
- 决策者: 用户（选择「出 ADR + 红测试后落地」）
- 关联: [ADR 0165](0165-everything-is-background-query-identity.md) · [ADR 0166](0166-streaming-transcript-persistence.md) · [DSG054 §9.3](../design/DSG054-主会话后台化Ctrl+B设计.md)
- 影响面: `ChatQueryScope`、`IChatContextManager`/`ChatContextManager`、`ISessionStore`/`SessionStore`、`ContextFoldService`、`ContextPersistenceService`、`RewindService`、`ChatService`、`EngineSession`、`EngineTurnClient`（新会话启动器）、`ClearCommand`

## 背景

`/clear` 现在的实现是 `ChatService.ClearHistoryAsync`（`ClearCommand.cs:61`、TUI 的
`StartNewConversation` → `EngineSession.cs:125`），只清**内存**消息列表：

- `sessionId` 由 `EngineSessionFactory` 生成一次后固定（`EngineSessionFactory.cs:141-164`），`/clear` 不改它；
- transcript 全部按 `{sessions}/{sessionId}/` 归目录：主记录 `transcript.jsonl`、
  子 agent 侧链 `subagents/{agentId}/transcript.jsonl`（`AgentTranscriptService.cs:147-156`）、
  ADR 0166 的旁路 `streaming.jsonl`。

于是「旧后台 task 的 transcript 怎么挂」的现状答案是：**挂在同一个 sessionId 目录下，没有任何代次区分**。
归属本身没错（都是同一会话），错的是**清空了却没有翻页**。

## 问题：三个洞

### 洞 1 — `/clear` 在磁盘上没兑现

`SessionResumeStep.cs:67`、`ResumeCommand.cs:358`、`HistorySeeder.cs:48`、`ForkCommand.cs:31`
四处置信方都是**整文件读**，不按代次过滤。清空后 resume 同一个 id，clear 之前的全部历史原样复活。
用户按 `/clear` 的预期是「这一段作废」，实际是「这一段只在本次进程内作废」。

### 洞 2 — `/clear` 会静默丢掉在途回合的 transcript

两个落盘点都是 `startCount` 差量：

- `TranscriptPersistMiddleware.cs:45-108`：进入时 `startCount = 快照计数`，结束后 `snapshot.Count <= startCount` 就 `return`；
- `SessionController.cs:710-731`：同样结构。

`/clear` 把列表清成 0，在途回合手里的 `startCount` 仍是清空前的 N，回合结束时 `snapshot.Count`（本轮新增 2）`<= N`
→ 静默 `return`，这一回合**一个字都不落盘**；ADR 0166 的旁路残稿还会被「正常结束」路径 `ClearStreamingAsync` 清掉。
用户正在等的那条查询，会被 `/clear` 连带抹掉记录。

### 洞 3 — 后台 task 跨代续写

`/clear` 时在跑的后台查询/子 agent 仍持旧 sessionId 出生快照（这一半是对的），但新代没换 id，
于是它们和「清空后的新对话」写同一文件。洞 1 修好（读端按代过滤）之前看不出来，修好之后会以
「旧 task 输出被当成新代内容」的形态暴露。

## 关键发现：光换 sessionId 不够

三个地方共同指向一个根因 —— **会话桶是「当前指针」，不是「查询持有」**：

1. `SessionStore.Log => GetOrAddLog(_sessionId)`（`SessionStore.cs:34`）：桶按当前指针解析；
2. `ChatContextManager` 的写入全部走 Actor（`_actor.Tell(new AddUserMessageCmd(...))`），**命令里不带 sessionId**；
   到 Actor 消费时 `AsyncLocal` 已离开，只能落到「当时的当前指针」；
3. `TranscriptPersistMiddleware` 落盘时同样实时读 `_contextManager.SessionId`（`:108`）。

所以 `/clear` 一旦换指针，在途回合的**消息**和在途回合的 **transcript** 都会跟到新代 —— 洞 2 换一种形态重现。

这与 ADR 0165 的结论同构：查询身份应该是 `(queryId, sessionId, ConversationTurn)` 三位一体，
**在查询开始时钉住**，而不是落盘时去读「当前值」。

## 决策

### 1. `ChatQueryScope` 增加 `SessionId`

```csharp
public string SessionId { get; }
public static ChatQueryScope Enter(string? queryId = null, int conversationTurn = 0, string? sessionId = null);
```

与 `QueryId`/`ConversationTurn` 同层，`Enter` 时快照。`ChatService` 三个 Send 方法在进入 scope 时
把 `_contextManager.SessionId` 传进去。

### 2. `ISessionStore` 支持按会话解析，默认仍是当前指针

不新增「视图对象」（会给 `ContextFoldExecutor` 造成接口连锁），而是给现有成员加可选参数：

```csharp
AppendOnlyLog GetLog(string? sessionId = null);
void Append(ApiMessage message, string? sessionId = null);
IReadOnlyList<ApiMessage> ToMessages(string? sessionId = null);
int CountOf(string? sessionId = null);
void CompactInPlace(IReadOnlyList<ApiMessage> messages, string? sessionId = null);
bool ReleaseSession(string sessionId);
```

`Log`/`Count`/`ToMessages()`/`Append(msg)` 保留原有含义（`sessionId = null`），既有调用点零改动；
`ContextFoldService` 里传 `AppendOnlyLog` 给 `FoldAsync` 的地方改成 `GetLog(sessionId)`，不击穿 `ContextFoldExecutor`。

### 3. Actor 命令带 sessionId

`AddUserMessageCmd` / `AddAssistantMessageCmd` / `AddToolResultCmd` / `AddAssistantToolCallCmd` /
`AddSystemMessageCmd` / `AddCompactSummaryCmd` / `AddToolResultWithBlocksCmd` / `ClearMessagesCmd` /
`LoadContextCmd` / `SaveContextCmd` / `GetMessageListCmd` / `FoldIfNeededCmd` / `Rewind*Cmd`
全部增加 `string SessionId` 字段，由 `ChatContextManager` 的公开方法入口用
`ChatQueryScope.CurrentOrNull?.SessionId ?? _sessionId` 解析后塞入。

**这是本 ADR 的核心**：命令在**调用线程**（AsyncLocal 还在）取值，Actor 只看命令字段，不再读「当前指针」。

### 4. `/clear` = 会话翻页，不再 `ClearHistoryAsync`

新会话启动器语义从「清当前桶」改为「开新代」：

```csharp
async Task BeginNewSessionAsync() {
    var newId = SessionIdGenerator.Generate();
    var oldId = contextManager.SessionId;
    contextManager.SwitchSession(newId);              // 只改默认指针，不碰旧桶
    await transcriptService.EnsureSessionAsync(newId, new SessionInfo { Id = newId, ProjectPath = cwd, ... });
    if (!任何在途查询) contextManager.ReleaseSession(oldId);   // 无在途则立刻回收旧桶
}
```

- 在途回合通过 `ChatQueryScope.SessionId` 钉住旧 id，消息继续写旧桶、transcript 继续落旧目录；
- 新提交的 scope 拿新 id，写新桶、落新目录；
- 旧代内容完整留在旧 sessionId 目录下，用户可 `/resume 旧id` 查看。

### 5. 读端一行不改

`SessionResumeStep` / `ResumeCommand` / `HistorySeeder` / `ForkCommand` 都只按 sessionId 读目录，
翻页天然让它们「只看一代」。这是选「换目录」而不是「同目录加 epoch 字段」的核心理由：
读端有五处，任何一处漏过滤都会让洞 1 复活。

### 6. `EngineSession.SessionId` 改为实时读

现在是 `_engine.SessionId`（工厂生成后固定的 init 属性）。翻页后若不改，新查询的 `SessionController`
会拿旧 id 写旧 transcript。改为读 `contextManager.SessionId` 实时值。

## 考虑过的替代方案

| 方案 | 放弃原因 |
|------|----------|
| 同目录 + `TranscriptEntry.Epoch` 字段 | 五个读端都要按 epoch 过滤，漏一处洞 1 就复活；`/clear` 的「不再出现」变成过滤结果而不是物理隔离 |
| 同目录 + 追加 `Type="clear"` 边界条目 | 是上一行的退化版；只解决主文件，`subagents/` 目录完全不受影响（洞 3 无法解） |
| `/clear` 时等所有在途查询结束再换代 | 后台查询可能跑几分钟，期间用户无法提交；且与「万物皆后台」裁决冲突 |
| `/clear` 时直接打断在途查询 | 同上，且用户失去正在等的结果 |
| 每查询一个 `IChatContextManager` 实例 | `LoadContextAsync`/系统提示词/工具规格都要重新加载，代价远大于带 sessionId |

## 后果

- 正面：洞 1/2/3 一次性闭合；`/clear` 语义在磁盘上兑现；旧代可通过 `/resume 旧id` 完整访问；读端零改动。
- 代价：`ChatContextManager` 的 13 个 Actor 命令加字段（机械改动）；`/clear` 多一次 `EnsureSessionAsync`。
- 内存：`SessionStore._logs` 只增不减。`ReleaseSession(sessionId)` 作为接口能力提供，但**两个调用点
  （斜杠 `/clear` 与 TUI 新会话启动器）都传 `ReleaseOldBucket = false`** —— 它们都拿不到可靠的在途查询数，
  误回收会让在途回合写进被 `GetOrAddLog` 重建的空桶（历史静默丢失，比多驻留一个桶贵得多）。
  一次 `/clear` 最多驻留一个旧桶，进程退出归还。宿主后续若能提供权威的在途计数（`EngineTurnClient.IsTurnRunning`），
  再打开这个开关。
- 不做：不做 transcript 文件的物理合并/清理；不做跨进程的会话代次同步。

## 验收

- `ChatQueryScopeTests`（cognition 域）：`Enter` 钉住 sessionId；并发查询各持不同代次。
- `SessionStore` 测试：同一 manager 下两个 sessionId 的桶互不串写；`ReleaseSession` 后重新添加是空桶。
- `ChatContextManager` 测试：scope 内 `AddUserMessageAsync` 落到 scope 的 sessionId，`SwitchSession` 之后
  仍在途的查询继续落旧桶；新查询落新桶。
- `TranscriptPersistMiddleware` 测试：`/clear`（换 sessionId）之后的在途回合 transcript 落旧目录，
  且不再因 `startCount` 错位而丢。
- TUI 手工（#101）：`/clear` 后 `ls ~/.jcc/sessions` 应新增目录；旧目录内容不变；`/resume 旧id` 能看到完整旧代。

## 已知缺口（实现期发现，不阻塞）

1. **`SessionStartHookState` 不随换代重置**：`EngineSession` 创建的共享标记让 TUI 主链的 `SessionStart` hook
   只跑一次，`/clear` 之后不会重跑。斜杠命令路径已显式调 `ISessionStartHookManager.OnSessionStartAsync(Source="clear")`，
   但 `SessionStartHookRunner` 那条链被标记抑制。修法是把标记也纳入代次（或换代时 `Interlocked.Exchange(..., 0)`），
   需要先确认两条链是否语义重复，避免 `/clear` 触发两次 hook。
2. **`workflow clear_history` MCP 工具仍走 `ClearHistoryAsync`**：它的语义是「清历史」而不是「开新会话」，
   与 `/clear` 不是同一件事，暂不纳入翻页。若后续要统一，把它接到 `SessionEpochSwitcher` 即可。
3. **`SessionStore._logs` 不回收**：见「后果」段的内存取舍。`ReleaseSession` 已提供能力，等宿主能给出
   权威在途查询数再打开。
