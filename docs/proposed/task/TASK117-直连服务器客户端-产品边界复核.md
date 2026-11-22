# TASK117 — 直连服务器客户端 / server mode（TASK103 §2.3 #49 取证）

> 状态：**判不适用 / 关闭（协议与产品形态不同）** · 日期 2026-11-22 · 总账编号：TASK103 §2.3 **#49 / TASK071-A3**
> 基准树：`D:\project\claude-code-rust\claude-code-rev-main\src`
> 本仓库：`D:\project\w2`

---

## 0. 结论（先看这里）

原文「直连服务器客户端（server 模式）」属实，但同样属于**基准云产品协议**，不是本仓可单独补齐的本地功能：

- 基准 `src/server/directConnectManager.ts:12-17/43-60`：`DirectConnectConfig{serverUrl,sessionId,wsUrl,authToken}`，`connect()` 用 Bearer token 开 WebSocket，承载 SDKMessage 与远程权限请求；`createDirectConnectSession.ts:22-40` 通过 `POST ${serverUrl}/sessions` 创建服务端会话并拿连接参数。即**服务端托管 agent 运行、本地只做显示与权限回传**。
- 本仓的远端通道是 bridge v2：`V2ReplBridgeTransport.cs:5-9`（SSE 读 + HTTP POST `/worker/*` 写）、`SessionIdCompat.cs:21-22`（worker 端点 `/v1/code/sessions/{id}/worker/*` vs 客户端 `/v1/sessions/{id}`）。这是 ADR 0141 定义的**自有 UI↔Engine 协议**，agent 在本机 Engine 进程运行。全仓 `directConnect`/`direct_connect` 零命中；`wsUrl` 仅出现在自有 bridge 的 URL 转换（`BridgeRemoteCore.V2Callbacks.cs:131`、`V1WorkPollSetupMiddleware.cs:177-181`），不是基准的 `POST /sessions` 直连托管契约。
- 两者都是"远程会话"，但**谁运行 agent、用什么协议、谁承担权限**都不同；把本仓 bridge 改叫直连服务不会带来新能力，只会制造协议混淆。按 #47/#48 同类判不适用，零代码。

---

## 1. 四问回答

### 问1 专家隐性实践
"远程会话客户端"有两种相反架构：①远端服务器运行 agent（本地瘦客户端，需会话创建 API + WS 消息流 + 权限回传）；②本地运行 agent、只把 UI/控制面放到别处（本仓 engine bridge）。前者的 token/WS/权限模型由服务端定义，无法只靠客户端侧代码对齐。

### 问2 原始素材（一手）
- 基准：`src/server/createDirectConnectSession.ts:1-40`（`POST /sessions`、`authToken`、`cwd`、`dangerouslySkipPermissions`）、`directConnectManager.ts:12-60/…`（WS + Bearer + SDKMessage/permission 回调）、`types.ts`（connectResponseSchema）。
- 本仓：`modules/async_primitives/transport_impl/bridge/v2/V2ReplBridgeTransport.cs:5-9/158-167`、`modules/async_primitives/transport.contracts/bridge/messages/SessionIdCompat.cs:21-22`、`modules/apps/engine/bridge/BridgeProtocol.cs:47-59`。
- 零产生点：`grep -rni "directConnect|direct_connect" modules --include=*.cs` 无命中；`wsUrl`/`serverUrl` 的命中均属自有 bridge 端点转换或 MCP OAuth 通用参数，不具备 `POST /sessions` + SDKMessage WS 的直连托管语义。

### 问3 具体方法与工具
读基准 server 目录三个文件确定协议面；全仓 grep 直连契约；对照 bridge v2 的端点与 ADR 0141 的本地 UI 协议定位，确认两者正交。

### 问4 独立验证来源
基准 `createDirectConnectSession` 需要服务端 `/sessions` 与 `wsUrl` 响应才能工作，独立证明它依赖服务端产品；本仓 `SessionIdCompat` 的 worker/客户端端点-split 独立证明本仓是自有协议体系。

---

## 2. 判定

| 维度 | 基准直连 server mode | 本仓 bridge v2 | 判定 |
|---|---|---|---|
| agent 运行位置 | 远端服务器 | 本机 Engine | 架构相反 |
| 协议 | POST /sessions + WS + SDKMessage | SSE 读 + POST /worker/* + BridgeProtocol | 不同 |
| 权限请求 | 远程权限回传到客户端 | 本地 UI 权限确认 | 不同 |
| 产生点 | 云产品专属 | 自有 bridge 完整实现 | 不适用 |

**若未来需要"服务端托管 agent"**：须先定义自有服务端会话 API 与 WS 协议，属新产品专项，不是补一个客户端类即可。

---

## 3. 验收（取证项，无代码改动）

- 全仓 `directConnect|direct_connect` 零命中，`wsUrl` 仅命中自有 bridge URL 转换；总账 #49 划掉并指向本文档。
