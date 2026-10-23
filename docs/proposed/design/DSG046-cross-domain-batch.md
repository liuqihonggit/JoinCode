# DSG046 — 跨域批次（compact 闭环 / 重试感知 / 权限持久化）

> 状态：proposed（TASK069 跨域登记项的实施方案 — 用户决策"全部做"后跨域设计）
> 来源：TASK069 差异文档跨域批次清单（E2/P1-P2/C4/C6/B-6）
> 锚点调研：引擎域代理已核对全部改造点（见下表）
> 基准：withRetry.ts / permissions.ts / permissionRuleParser.ts / autoCompact.ts / compact.ts

## 一、改造锚点总表（调研实证）

| 项 | 引擎侧现状 | 改造锚点 | 基准参照 |
|----|-----------|---------|---------|
| C6 压缩完成事件 | BridgeProtocol 21 种事件无 compact；CompactCommand.cs:59 执行完仅 WriteLine | CompactCommand 经 ChatCommandResult 携带 CompactSummaryData → EngineSession Dispatch 投事件；双侧加 CompactCompleted | autoCompact.ts:313-321 |
| C4 自动压缩接线 | AutoCompactService.ShouldAutoCompact 齐备无调用方；回合末 SessionController.cs:133 | PostProcessMainAgentAsync 后同源 token 判定 → CompactAsync 异步（防阻塞 idle）+ 复用 C6 事件回报 | autoCompact.ts:241-351 |
| E2 重试感知 | **QueryEngine.cs:172-185 已有静默重试（IsRetryable+指数退避）**——域5"零重试"系误判；真正差异 = 重试静默无 UI 感知 | QueryEngine catch 处发 retry 回调 → IEventConsumer 扩 OnRetry → BridgeEvent 扩 Retry 事件 → TUI spinner 提示 | withRetry.ts:431-441 |
| P1-P2 权限持久化 | TuiPermissionConfirmationHandler 仅内存（AlwaysAllow 30min）；IConfigurationService.SetAsync 先例在 | abs_guard/security/permission/ 新 PermissionRuleParser；AlwaysAllow 分支 SetAsync 规则串；CheckPermissionAsync 判定链前段加载 | permissionRuleParser.ts:93-133 |

## 二、批次划分

| 批 | 内容 | 协议新增 | 域 |
|----|------|---------|----|
| 1 | C6 CompactCompleted 事件（双侧 + CompactCommand 发射 + TUI 渲染）+ C4 自动压缩接线（复用事件回报）+ C6' TUI 压缩警告抑制（Reset 接线到事件） | CompactCompleted(MessagesSummarized, OriginalTokens, CompressedTokens, IsAuto) | 引擎+协议+TUI |
| 2 | E2 Retry 事件 + QueryEngine 回调 + TUI 重试提示（spinner 行 "Retrying in Xs (N/M)"——非 ErrorMessageItem） | Retry(Reason, Attempt, MaxRetries, DelayMs) | 引擎+协议+TUI |
| 3 | P1-P2 权限规则持久化：PermissionRuleParser（"Tool"/"Tool(content)" 格式+转义）+ AlwaysAllow → settings.json + 加载判定链 + 热加载 | 零（走 settings.json） | 引擎+TUI 宿主 |

B-6（--continue 跨项目过滤）为独立小修随批 1 顺带（SessionResumeStep.cs:83 加 ProjectPath 过滤）。

## 三、关键设计决策

- **重试层不新包**：QueryEngine 已有退避（防双层重试放大）——E2 只做"感知上屏"不做重试本体
- **C4 异步执行**：CompactAsync 含 LLM 调用（摘要生成）——回合终态后异步跑，防阻塞 idle 状态
- **P1-P2 单一数据源**：规则写入键 = 加载键（PermissionRuleParser Parse/Format 双向同一实现，禁止双向维护）
- **协议镜像纪律**：BridgeProtocol.cs（引擎）与 BridgeEvent.cs（TUI）双侧 [JsonDerivedType] 同步注册 + JsonContractTests 锁形状（BridgeProtocolTests 先例）
- **事件通道复用**：CompactCommand 无事件通道——借 EngineSession.DispatchSlashCommandAsync 的 consumer 投影（避免命令层穿透 DI）

## 四、验收

| 批 | 单测 | 回归 |
|----|------|------|
| 1 | CompactCompleted 协议镜像/事件路由/警告抑制 ≥6 用例 + AutoCompactService 接线 ≥3 | 引擎域 cognition+composition 测试 + TUI 全量 + selftest |
| 2 | Retry 事件路由 + spinner 提示 ≥4 | 同上 |
| 3 | PermissionRuleParser Parse/Format 往返 + 持久化加载 ≥8 | 同上 + guard 域测试 |

## 五、决策记录

- E2 范围修正：重试本体已存在（域5 误判修正），只做 UI 感知——工作量大幅缩小
- 批次顺序按闭环价值：压缩闭环（C4+C6 联动）> 重试感知 > 权限持久化（最大独立）
- B-6 顺带批 1（最小改动单行过滤）
