# TASK115 — upstreamproxy（TASK103 §2.3 #47 取证）

> 状态：**判不适用 / 关闭** · 日期 2026-11-22 · 总账编号：TASK103 §2.3 **#47 / TASK071-A1**
> 基准树：`D:\project\claude-code-rust\claude-code-rev-main\src`
> 本仓库：`D:\project\w2`

---

## 0. 结论（先看这里）

原文「upstreamproxy（CONNECT→WS 隧道中继，Windows 产品可能 N/A）」属实，且"可能"可以坐实为**不适用**：

- 基准 `src/upstreamproxy/upstreamproxy.ts:1-17` 明确限定"**container-side wiring** in a CCR session container"：读 `/run/ccr/session_token`（`:26`）、`prctl(PR_SET_DUMPABLE, 0)`（`:6`）、下载 CA 拼进 `/etc/ssl/certs/ca-certificates.crt`（`:9`）、导出 `HTTPS_PROXY`/`SSL_CERT_FILE` 等环境变量（`:185-199`）；配套 `relay.ts`(455 行) 实现 localhost CONNECT → WebSocket 中继。两个文件共 740 行。
- 本仓无任何 `/run/ccr`、`session_token`、`PR_SET_DUMPABLE`、`ca-certificates` 契约（grep 零命中）。本仓的 CCR 是**桥接客户端**（`modules/async_primitives/transport_impl/bridge/v2/V2ReplBridgeTransport.cs:5-9` 的 SSE 读 + `CCRClient` 写 `/worker/*`），与"容器内出站 MITM 代理"是正交的两件事。
- 这是**平台/部署形态专属子系统**（Linux 容器 + CCR 容器侧 token），不是本仓 Windows 桌面/CLI 产品的缺失能力。按 #35/#36 同类判不适用，零代码。

---

## 1. 四问回答

### 问1 专家隐性实践
远程会话产品的"出站代理"分两种：①客户端侧的 HTTP(S)_PROXY 配置（用户可控、跨平台）；②容器侧的透明 MITM 中继（读容器注入的短期 token + 系统 CA + 本地 CONNECT 监听）。后者依赖容器运行时契约，不能脱离 `/run/ccr` 与 Linux prctl 单独移植；没有部署前提时实现它等于写一个永远不启动的服务。

### 问2 原始素材（一手）
- 基准：`src/upstreamproxy/upstreamproxy.ts:1-17/26/185-199`（容器前提、token 路径、环境变量导出）、`src/upstreamproxy/relay.ts:3-10/25-30/319/379-386`（CONNECT 解析、WS 隧道、绕过代理的 host 清单）。
- 本仓：`modules/async_primitives/transport_impl/bridge/v2/V2ReplBridgeTransport.cs:5-9/158-167`（CCR worker 读写通道）、`modules/abstractions/abstractions_primitives_configuration/src/app_data/JccEnvVar.cs:105`（`JCC_BRIDGE_USE_CCR_V2`）——只有桥接客户端开关，无容器代理开关。

### 问3 具体方法与工具
先读基准模块头注释确定部署前提，再全仓 grep 关键路径/系统调用/环境变量名验证本仓无产生点；对照本仓 CCR 实现确认名称相同但域不同。

### 问4 独立验证来源
基准 `relay.ts:10` 注释解释为何用 WS 而非裸 CONNECT（GKE L7 ingress 限制），再次绑定其容器/云基础设施语境；本仓 bridge v2 的 SSE+POST 通道在该语境下无需本地代理。

---

## 2. 判定

| 维度 | 基准 upstreamproxy | 本仓 | 判定 |
|---|---|---|---|
| 部署前提 | CCR 会话容器（Linux） | Windows 桌面/CLI + 桥接客户端 | 不适用 |
| 凭据来源 | `/run/ccr/session_token`（读后即删） | 无此契约 | 无产生点 |
| 网络形态 | localhost CONNECT → WS → CCR ingress | SSE 读 + HTTP POST 写 /worker/* | 正交 |
| 代码规模 | 740 行独立子系统 | 0 | 不是"漏接线" |

**若未来要在容器中部署本产品**，需先有容器侧 token 注入与 CA bundle 契约，再立专项实现；当前无前置条件。

---

## 3. 验收（取证项，无代码改动）

- `grep -rn "/run/ccr|SESSION_TOKEN_PATH|ca-certificates|PR_SET_DUMPABLE" modules --include=*.cs` 零命中。
- 总账 #47 划掉并指向本文档。
