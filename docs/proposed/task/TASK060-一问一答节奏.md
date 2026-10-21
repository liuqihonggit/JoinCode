# TASK060 — 一问一答节奏（忙时排队 + 未登录友好提示）

> 状态：in_progress（2026-10-21 开工）
> 前置：TASK059 完结；用户真机反馈（对照基准截图）："没有登录它提示多友好啊，而且消息区域是一问一答，我的提问倒是连续发送了两个"

## 痛点（用户真机截图分析）

| 现象 | 根因 |
|------|------|
| `❯ 112` 40s 无响应后 `❯ 3333` 又提交，两条提问堆叠无回答夹中间 | 忙时提交被忽略（EngineTurnClient 回 Error 诱导行），一问一答节奏破坏 |
| footer "未登录"是硬编码种子，未登录时输入仍调引擎傻等 | 登录态未从引擎真实获取（session_info 无此字段） |

## 方案（两步渐进）

### Step 1：忙时排队（Core 层，对齐基准 queued 语义）
- `ReplShell` 加排队队列：忙时 Submit → 用户输入上屏 + `⎿ 已排队` dim 行（不调引擎）；
  回合结束（DrainEngine 检测 !IsBusy）自动按序发送队首（不上屏重复）
- 新消息组件 `InlineNoteItem`（`  ⎿  ` 前缀 dim 行）— queued 提示与未登录提示复用
- EngineTurnClient 忙拒逻辑保留兜底（不再被 Core 拦截路径触达）

### Step 2：未登录友好提示（引擎→TUI 全链）
- `BridgeEvent.SessionInfo` 加 `LoggedIn`（bool，末尾可选零破坏）
- 引擎侧 `BuildSessionInfoEvent` 判定：`config.Provider.ApiKey` 非空 = 已登录（key 可用即能对话）
- TUI：driver 应用 → FooterState.LoginError 真实化；未登录时 Submit 本地回
  `⎿ 未登录 · 请配置 auth.json 或运行 /login`（不调引擎不傻等）

## 验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| InlineNoteItem（⎿ dim 行） | queued/未登录提示复用 | 待做 | 待真机 |
| 忙时排队队列（ReplShell） | Submit/回合结束自动发送 | 待做 | 待真机 |
| SessionInfo.LoggedIn 字段链 | EngineSession 判定 + driver footer | 待做 | 待真机 |
| 未登录本地即时反馈 | Submit 拦截 | 待做 | 待真机 |
