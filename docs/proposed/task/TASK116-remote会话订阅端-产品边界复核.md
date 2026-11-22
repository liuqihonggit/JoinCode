# TASK116 — remote 会话订阅端 / CCR viewer（TASK103 §2.3 #48 取证）

> 状态：**判不适用 / 关闭（云服务产品边界）** · 日期 2026-11-22 · 总账编号：TASK103 §2.3 **#48 / TASK071-A2**
> 基准树：`D:\project\claude-code-rust\claude-code-rev-main\src`
> 本仓库：`D:\project\w2`

---

## 0. 结论（先看这里）

原文「remote 会话订阅端（CCR viewer）」属实，但它是**云端产品形态**而非本地 CLI 缺的功能：

- 基准 `src/remote/RemoteSessionManager.ts:49-61` 的 `RemoteSessionConfig` 要求 `sessionId + getAccessToken() + orgUuid`，`viewerOnly=true` 时"Ctrl+C/Escape 不发送中断、关闭 60s 重连超时、不改会话标题——**Used by `claude assistant`**"（`:55-59`）。配套 `SessionsWebSocket.ts`/`remotePermissionBridge.ts`/`sdkMessageAdapter.ts` 构成 OAuth + 组织维度 + 远程权限回传的 viewer 客户端。
- 本仓的 "remote" 是 engine bridge：`modules/apps/engine/bridge/BridgeProtocol.cs:47-59`（本地 UI 协议事件）、`modules/async_primitives/transport_impl/bridge/v2/V2ReplBridgeTransport.cs:5-9`（SSE 读 + HTTP POST `/worker/*`）。本仓 worker 桥也带组织标识（`BridgeApiClient.cs:255-256/347-348` 的 `orgUUID` + `x-organization-uuid` 头）与 token 刷新（`BridgeTokenRefreshScheduler.cs:146`），但**没有 `viewerOnly` / 只读订阅语义 / `RemoteSessionManager`**（三者全仓 grep 零命中）：worker 桥是"我的会话被远端控制"，viewer 是"我只看别人的会话"。
- 与 #47 类似：这是服务端产品（组织账号 + Web/桌面 viewer）的客户端，不是本机 CLI 可单独补齐的能力。按无产生点 + 产品边界判不适用，零代码。

---

## 1. 四问回答

### 问1 专家隐性实践
远程 viewer 的核心是**权限边界**：只读客户端不得发 interrupt、不得改标题、权限请求需经远程桥回传到操作者；这些语义由服务端组织/OAuth 上下文定义，不可能靠本地改动"猜"出来。把本地 engine bridge 强行标成 viewer 只会造成"看起来连上了但行为不受控"。

### 问2 原始素材（一手）
- 基准：`src/remote/RemoteSessionManager.ts:49-61/325-355`（config 字段与 viewerOnly 注释）、`SessionsWebSocket.ts`/`remotePermissionBridge.ts`/`sdkMessageAdapter.ts`（订阅/权限/消息适配三件套）。
- 本仓：`modules/apps/engine/bridge/BridgeProtocol.cs:47-59`、`modules/async_primitives/transport_impl/bridge/v2/V2ReplBridgeTransport.cs:5-9/158-167`、`modules/async_primitives/transport_impl/bridge/shared/auth/BridgeTokenRefreshScheduler.cs:146-151`（worker 通道 token，非 OAuth viewer）。

### 问3 具体方法与工具
读基准 config 定义与 viewerOnly 注释确定产品归属（"Used by claude assistant"），再全仓 grep `orgUuid`/`viewerOnly`/远程订阅 API 验证本仓零产生点；对照 bridge v2 的读写通道确认名称相近但域不同。

### 问4 独立验证来源
基准 remote 目录的 4 个文件互相印证订阅客户端结构；本仓 ADR 0141（engine/bridge 为本地 UI 协议）与 TASK071 原文"engine/bridge 与 TS bridge 正交"互证两者不是同一产品线。

---

## 2. 判定

| 维度 | 基准 remote viewer | 本仓 | 判定 |
|---|---|---|---|
| 身份 | OAuth + orgUuid（viewer 侧） | worker session token + orgUUID 头 | 同组织体系、不同角色 |
| 权限 | 只读（不 interrupt/不改标题） | 本地 UI 对 Engine 双向控制 | 正交 |
| 通道 | SessionsWebSocket + 权限桥 | engine bridge（本地）+ SSE/POST worker | 不同协议 |
| 产生点 | `claude assistant` 专用 | 全仓零 viewerOnly/orgUuid | 不适用 |

**若未来要做 viewer**：需先定义本产品自己的组织/权限模型与只读会话协议，属新产品的专项立项，不是本项可修的"漏接线"。

---

## 3. 验收（取证项，无代码改动）

- 全仓 `grep viewerOnly|viewer_only|pure viewer|RemoteSessionManager|SessionsWebSocket` 零命中（`orgUUID` 命中属 worker 桥，见 §0）；总账 #48 划掉并指向本文档。
