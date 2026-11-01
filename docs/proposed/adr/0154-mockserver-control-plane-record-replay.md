# ADR 0154: MockServer 控制面 + 录制回放

## 状态

proposed

## 背景

当前 MockServer 是**脚本化静态回放**——启动时加载 JSON，按顺序消费 `ScriptedTurn`，不支持运行时动态控制。调试 jcc 时只能停服→改 JSON→重启，无法实时注入响应或录制真实交互。

用户诉求：用一个端口，所有 jcc 实例对接这个端口，AI 能通过自由操作这个端口收发消息，实现无 AI 调试。核心目的除了得到效果，还要处理 bug——因此 jcc 走的 HTTP 路径必须和真实环境完全一致，bug 路径不偏移。

## 决策

在现有 MockServer（Kestrel HTTP 服务器）上加**控制面端点**（`/__control__/*` 路径前缀），同端口服务，不分离控制通道。

### 控制面 API

| 端点 | 方法 | 作用 |
|------|------|------|
| `/__control__/state` | GET | 当前状态（模式、请求数、注入队列长度） |
| `/__control__/requests` | GET | 所有捕获的请求 JSON |
| `/__control__/inject` | POST | 注入一个响应（下次 LLM 请求返回这个） |
| `/__control__/clear` | POST | 清空请求历史和注入队列 |
| `/__control__/script` | POST | 运行时替换脚本 |
| `/__control__/record/start` | POST | 开始录制（透传真实 LLM + 录下交互） |
| `/__control__/record/stop` | POST | 停止录制，返回录制文件 |
| `/__control__/replay/load` | POST | 加载录制文件进入回放模式 |

### 三种工作模式

1. **回放**（默认）— 按脚本/录制文件顺序回放，不连真实 LLM
2. **录制** — 透传给真实 LLM + 录下请求/响应对，录完可回放
3. **交互** — 我实时看 requests + inject 决定回什么，间隔泵轮询

### 注入优先逻辑

LLM 请求到达时：先检查注入队列 → 有注入则直接返回注入响应 → 无注入则走脚本策略。注入响应支持流式（`streamChunks[]`）和非流式（`body`）。

## 替代方案

1. **单独控制端口** — 否决：控制通道与 LLM 通道分离会导致 jcc 走的代码路径与真实环境不同，bug 路径偏移
2. **命名管道控制** — 否决：同上，且实现更复杂
3. **进程内 Mock（`JCC_HTTP_MODE=Mock`）** — 已有但不满足需求：需要写代码 `SetupResponse()`，不是端口外部可控，无法跨进程

## 影响

- MockServer.Core 新增 `MockServerController` 类管理控制状态
- KestrelMockServer 新增 `/__control__/*` 端点注册
- LLM 请求处理接入注入优先逻辑
- 录制模式需 HttpClient 透传 + 交互序列化
