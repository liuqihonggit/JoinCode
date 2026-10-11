# 撤回本条所在轮 — 前后端对齐

## 背景

右键菜单"✕ 删除本条"只从 UI `Messages` 集合移除单条消息，**不调引擎撤回**，导致前后端脱节：
- UI 层：少一条消息
- 引擎层：上下文残留该消息（下次发送仍带这条历史）

用户要求："撤回最近一条消息时，对应的卡片链条都要撤回" — 即撤回操作必须前后端一致。

## 现状调研

### 引擎层"一轮"定义

`AppendOnlyLog.TrimLastTurn()` (`kit/brain/cache/AppendOnlyLog.cs:65-81`)：
- 从末尾向前找最后一条 `User` 消息
- 移除该 `User` + 其后所有消息（Assistant/Tool 等）
- **一轮 = 最后一条 User + 其后所有消息**

### UI 层"一轮"定义

`RegenerateLastReplyAsync` (`MainViewModel.Messages.cs:198-215`)：
- 找最后一条 `User` 消息（非空）
- 调 `RewindLastTurnAsync` + UI 移除该 User 到末尾所有消息
- **前后端一致** ✅

### 不一致点

`RemoveMessage` (`MainViewModel.Messages.cs:191-196`)：
- 只从 UI `Messages.Remove(message)`，**不调引擎**
- **前后端脱节** ❌

## 方案

**把"✕ 删除本条"改为"⤺ 撤回本条所在轮"**

行为：
1. 右键任意消息 → 找到该消息所属轮次起点（向前找最近 `User` 消息）
2. 计算从该轮到末尾有多少轮（数 User 消息数）
3. 调引擎 `RewindLastTurnAsync` N 次（逐轮撤回）
4. UI 移除从该轮起点到末尾的全部消息
5. 把该轮 User 消息内容放回输入框（不重发，用户可编辑后重发）

### 为什么多次调 RewindLastTurnAsync 而非 RewindToMessageIndexAsync？

- `IJccChatSession` 接口只有 `RewindLastTurnAsync`，没有 `RewindToMessageIndexAsync`
- 添加 `RewindToMessageIndexAsync` 需更新 10 个实现类（1 生产 + 1 占位 + 8 测试 mock）
- 多次调 `RewindLastTurnAsync` 语义清晰（每次撤回最后一轮），无需改接口
- `TrimLastTurn` 在无 User 消息时返回 0（幂等安全），多次调用无副作用

## 任务清单

- [ ] 修改 `MainViewModel.Messages.cs`：`RemoveMessage` → `RewindTurnAtAsync`
- [ ] 修改 `MainWindow.axaml`：菜单文案 + Command 绑定
- [ ] 编译验证
- [ ] 运行测试
- [ ] git 提交

## 验证

- 编译通过（0 错误 0 警告）
- gui 测试全通过
- 手动验证：右键中间某轮消息 → 该轮及之后所有消息撤回 + 引擎上下文同步 + 输入框恢复该轮 User 内容
