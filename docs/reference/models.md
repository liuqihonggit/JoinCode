# 可用模型列表

> 此文档从 README 摘出，详细列出所有预置模型。

配置文件 `~/.jcc/settings.json` 预置了 5 个供应商共 43 个模型条目（跨供应商去重后 42 个独立模型，`deepseek-v4-flash` 在 DeepSeek 与 SenseNova 下各声明一次），按供应商分组如下：

## 供应商协议与端点汇总

| 供应商 | `protocol` | `endpoint` | `modelsEndpoint` | API Key 环境变量 | 默认模型 |
|--------|------------|------------|------------------|------------------|----------|
| `deepseek` | `openai-compatible` | 内置（兼容 OpenAI 协议） | —（不拉取远程列表） | `DEEPSEEK_API_KEY` | `deepseek-v4-flash` |
| `openai` | `openai-compatible` | `https://api.openai.com/v1` | `models` | `OPENAI_API_KEY` | `gpt-5.6-sol` |
| `anthropic` | `anthropic` | `https://token.sensenova.cn/v1`（经 SenseNova 中转） | `models` | `ANTHROPIC_API_KEY` | `claude-opus-5-20250815` |
| `sensenova` | `openai-compatible` | `https://token.sensenova.cn/v1` | `models` | `SENSENOVA_API_KEY` | `sensenova-6.7-flash-lite` |
| `agnes` | `openai-compatible` | `https://apihub.agnes-ai.com/v1` | `models` | `AGNES_API_KEY` | `agnes-2.0-flash` |

> **字段说明**
> - **`protocol`**：`openai-compatible` 表示走 OpenAI Chat Completions 协议；`anthropic` 表示走 Anthropic Messages 协议。
> - **`endpoint`**：API 基址。`deepseek` 留空表示使用内置默认地址；`anthropic` 此处指向 SenseNova 中转地址，可按需改为官方 `https://api.anthropic.com`。
> - **`modelsEndpoint`**：列出模型的子路径（如 `GET {endpoint}/models`）。留空表示不从远端拉取模型列表，仅使用配置中静态声明的模型。
> - **`autoFetchModels`**：根级配置为 `true`，表示对支持 `modelsEndpoint` 的供应商启动时自动拉取远端模型清单合并到本地。

## DeepSeek（默认，2 个模型）

| 模型 ID | 别名 | 上下文 | 说明 |
|---------|------|--------|------|
| `deepseek-v4-flash` | `flash`、`v4`、`chat` | 1M | 快速模型，支持思考模式（默认） |
| `deepseek-v4-pro` | `pro` | 1M | 旗舰模型，支持思考模式 |

## OpenAI（18 个模型）

| 模型 ID | 别名 | 上下文 | 说明 |
|---------|------|--------|------|
| `gpt-4o-mini` | `4o-mini` | 128K | 快速低成本模型 |
| `gpt-4o` | `4o` | 128K | 多模态模型 |
| `gpt-4.1-nano` | `4.1-nano` | 1M | 最快最便宜，1M 上下文 |
| `gpt-4.1-mini` | `4.1-mini` | 1M | 高效平衡，1M 上下文 |
| `gpt-4.1` | `4.1` | 1M | 最新旗舰，1M 上下文 |
| `o4-mini` | `o4-mini` | 200K | 高效推理模型 |
| `o3-mini` | `o3-mini` | 200K | 低成本推理模型 |
| `o3` | `o3` | 200K | 深度推理模型 |
| `o3-pro` | `o3-pro` | 200K | O3 推理旗舰模型 |
| `gpt-5.6-sol` | `5.6`、`sol` | 1.05M | GPT-5.6 旗舰，1M 上下文 |
| `gpt-5.6-terra` | `terra` | 1.05M | GPT-5.6 平衡版，1M 上下文 |
| `gpt-5.6-luna` | `luna` | 1.05M | GPT-5.6 快速版，1M 上下文 |
| `gpt-5.4` | `5.4` | 1.05M | GPT-5.4 旗舰，1M 上下文 |
| `gpt-5.4-mini` | `5.4-mini` | 400K | GPT-5.4 Mini，400K 上下文 |
| `gpt-5.4-nano` | `5.4-nano` | 400K | GPT-5.4 Nano，400K 上下文 |
| `gpt-5.4-image-2` | `image2` | 272K | GPT-5.4 图片生成模型 |
| `gpt-audio` | `audio` | 128K | GPT 语音对话模型 |
| `gpt-audio-mini` | `audio-mini` | 128K | GPT 语音对话快速版 |

## Anthropic（11 个模型）

| 模型 ID | 别名 | 发布日期 | 上下文 | 说明 |
|---------|------|----------|--------|------|
| `claude-mythos-5` | `mythos5`、`best` | 2025-08-15 | 1M | Claude Mythos 5 旗舰模型 |
| `claude-opus-5` | `opus5` | 2025-08-15 | 1M | Opus 5，最强推理 |
| `claude-sonnet-5` | `sonnet5` | 2025-08-15 | 1M | Sonnet 5 |
| `claude-fable-5` | `fable5` | 2025-08-15 | 1M | Fable 5 创意写作模型 |
| `claude-opus-4-8` | `opus4.8` | 2025-07-15 | 1M | Opus 4.8 |
| `claude-opus-4-7` | `opus` | 2025-07-01 | 1M | Opus 4.7 |
| `claude-opus-4-6` | — | 2025-05-14 | 1M | Opus 4.6 |
| `claude-sonnet-4-6` | `sonnet` | 2025-05-14 | 200K | Sonnet 4.6，平衡性能与速度 |
| `claude-sonnet-4-5` | — | 2025-09-29 | 200K | Sonnet 4.5 |
| `claude-opus-4-5` | — | 2025-11-01 | 200K | Opus 4.5 |
| `claude-haiku-4-5` | `haiku` | 2025-10-01 | 200K | 快速低成本模型 |

## SenseNova（5 个模型）

| 模型 ID | 别名 | 上下文 | 说明 |
|---------|------|--------|------|
| `sensenova-6.7-flash-lite` | `flash-lite`、`6.7` | 128K | 轻量多模态智能体模型，支持文本对话与图像输入理解 |
| `sensenova-6.8-flash-lite` | — | — | 轻量快速模型 |
| `sensenova-u1-fast` | `u1`、`infographics` | 128K | 信息图（Infographics）生成模型 |
| `deepseek-v4-flash` | `ds`、`v4` | 1M | 通过商汤平台调用 DeepSeek，支持思考模式、1M 上下文、工具调用 |
| `glm-5.2` | — | — | 轻量模型 |

## Agnes（7 个模型）

| 模型 ID | 别名 | 上下文 | 说明 |
|---------|------|--------|------|
| `agnes-2.0-flash` | `flash2` | 128K | 新一代快速模型 |
| `agnes-2.5-flash` | — | — | Agnes 2.5 Flash |
| `agnes-2.5-pro` | — | — | Agnes 2.5 Pro |
| `agnes-2.5-pro-alpha` | — | — | Agnes 2.5 Pro Alpha |
| `agnes-image-2.0-flash` | `image` | 128K | 图像理解模型 |
| `agnes-image-2.1-flash` | — | 128K | 新一代图像模型 |
| `agnes-video-v2.0` | `video` | 128K | 视频理解模型 |

---

交互模式下可通过 `/model <别名或ID>` 快速切换模型，例如 `/model flash`、`/model pro`、`/model mythos5`、`/model opus5`、`/model sonnet`、`/model 5.6`。
