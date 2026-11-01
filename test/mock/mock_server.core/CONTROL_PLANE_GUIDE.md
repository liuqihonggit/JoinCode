# MockServer 控制面使用指南

## 快速启动

```bash
# 1. 启动 MockServer（OpenAI 协议）
JoinCode.OpenAI.MockServer.exe --port 9901 --config mockserver.json

# 2. jcc 对接 MockServer（不连真实 AI）
export JCC_ENDPOINT=http://localhost:9901
export OPENAI_API_KEY=sk-test-mock
export JCC_VENDOR=openai
jcc chat "你好"
```

## 控制面端点 API

所有端点在**同一端口**的 `/__control__/*` 路径下。jcc 走真实 LLM 路径，bug 路径不偏移。

### 状态查询

```bash
# 当前状态（模式、请求数、注入队列长度）
curl http://localhost:9901/__control__/state
# → {"mode":"Interactive","total_requests":3,"inject_queue_length":0,"recorded_interactions":0}

# 查看所有捕获的请求（jcc 发了什么给 LLM）
curl http://localhost:9901/__control__/requests
# → [{"method":"POST","path":"/v1/chat/completions","body":"...","index":0}]
```

### 注入响应（交互式调试）

```bash
# 注入一个响应 — 下次 jcc 发 LLM 请求时直接返回这个
curl -X POST http://localhost:9901/__control__/inject \
  -H "Content-Type: application/json" \
  -d '{"status_code":200,"content_type":"application/json","body":"{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"模拟 AI 回复\"}}]}"}'

# 注入错误响应 — 测试 jcc 错误处理
curl -X POST http://localhost:9901/__control__/inject \
  -d '{"status_code":429,"body":"{\"error\":\"rate limited\"}"}'

# 注入流式响应
curl -X POST http://localhost:9901/__control__/inject \
  -d '{"is_stream":true,"stream_chunks":["data: {\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}\n\n","data: [DONE]\n\n"]}'

# 批量注入脚本
curl -X POST http://localhost:9901/__control__/script \
  -d '[{"body":"第一轮回复"},{"body":"第二轮回复"},{"status_code":500,"body":"错误"}]'

# 清空
curl -X POST http://localhost:9901/__control__/clear
```

### 录制回放

```bash
# 开始录制 — 透传给真实 LLM + 录下交互
curl -X POST http://localhost:9901/__control__/record/start \
  -d '{"upstream":"https://api.openai.com/v1","api_key":"sk-real-key","vendor":"openai"}'

# 此时 jcc 的请求会透传给真实 OpenAI，同时录下请求/响应
jcc chat "帮我写个函数"

# 停止录制
curl -X POST http://localhost:9901/__control__/record/stop
# → {"interaction_count":3,"file_path":null}

# 加载录制回放 — 不再连真实 AI
curl -X POST http://localhost:9901/__control__/replay/load \
  -d '{"created_at":"...","upstream":"...","vendor":"openai","interactions":[...]}'

# 此时 jcc 的请求会按录制顺序回放
jcc chat "帮我写个函数"  # 收到与录制时相同的响应
```

### 模式切换

```bash
# 切换到交互模式（默认）
curl -X POST http://localhost:9901/__control__/mode/interactive
```

## 间隔泵轮询模式

AI 助手通过轮询 `/__control__/requests` 侦听 jcc 发的请求，然后通过 `/__control__/inject` 决定回什么：

```
循环:
  1. GET /__control__/state → 检查 total_requests 是否增加
  2. GET /__control__/requests → 获取最新请求，看 jcc 发了什么
  3. POST /__control__/inject → 根据请求内容注入响应
  4. 等待间隔（如 100ms）
```

这样 AI 可以在不调用真实 LLM 的情况下，动态操控 jcc 的所有行为路径。

## 三种工作模式

| 模式 | 行为 | 用途 |
|------|------|------|
| `Interactive`（默认） | 注入优先，无注入走脚本 | 探索性调试，AI 实时操控 |
| `Recording` | 透传真实 LLM + 录下交互 | 录制真实交互供回放 |
| `Replay` | 按录制文件回放，不连真实 LLM | 反复调试同一场景 |

## jcc 环境变量配置

| 变量 | 值 | 说明 |
|------|-----|------|
| `JCC_ENDPOINT` | `http://localhost:9901` | MockServer 端点 |
| `OPENAI_API_KEY` | `sk-test-mock` | MockServer 不校验 |
| `JCC_VENDOR` | `openai` | 选协议 |
| `JCC_PROTOCOL` | `openai-compatible` | 协议覆盖 |
| `JCC_MODEL_ID` | `gpt-4o` | 任意值 |
