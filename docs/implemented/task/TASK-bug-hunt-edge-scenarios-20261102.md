# 边缘场景 Bug Hunt — 2026-11-02

## 测试方法
三件套：MockServer（Interactive 模式）+ mock_ai_responder（自动响应）+ jcc（被测对象）
AI 用 bash 调 `jcc -p "消息"`，responder 自动匹配意图并 inject 响应。

## 测试结果汇总

| 场景 | 关键词 | 结果 | 说明 |
|------|--------|------|------|
| F2 工具执行慢 | f2 | ✅ | bash sleep 10，jcc 等待 11.6s 完成 |
| F3 多轮工具调用 | f3 | ✅ | 2 轮 read 失败 → 最终回应 |
| H4 Unicode/emoji | h4 | ✅ | 多语言+emoji 正确处理 |
| I1 空工具调用 | i1 | ✅ | 空回复报错是合理行为（非 bug） |
| I2 多个工具调用 | i2 | ✅ | 一次 2 个 tool_calls 依次执行 |
| I3 工具执行错误 | i3 | ✅ | bash exit 1 → LLM 收到错误 → 恢复 |
| J1 超长响应 | j1 | ✅ | 100 行 4607 字符 3.5s 完成（修复后） |
| K1 参数格式错误 | k1 | ✅ | JSON 缺引号容错处理 |
| K2 参数缺失 | k2 | ✅ | 缺 file_path 引导提示 |
| L4 流式中断 | l4 | ✅ | 无 [DONE] 优雅提取已收内容 |
| L6 超长工具结果 | l6 | ✅ | seq 1 500，2522 字符 |
| L10 空消息 | (空) | ✅ | exitCode=3 + 引导提示 |
| L11 参数特殊字符 | l11 | ✅ | 引号/换行/制表/反斜杠 |
| L12 参数 Unicode | l12 | ✅ | 中文/日文/韩文/emoji |
| M1 --continue 上下文 | hello→--continue | ✅ | 第二轮包含第一轮 assistant 消息 |
| M3 5轮工具调用 | m3 | ✅ | 5轮 read 失败 → 最终回应，exitCode=0 |
| M4 tool_call_id 匹配 | m4 | ✅ | jcc 正确传递 tool_call_id |
| M5 嵌套工具调用 | m5 | ✅ | bash echo → bash echo → 最终回应 |
| M6 JSON/NDJSON 统一 | --format json/ndjson | ✅ | 字段名统一为 content（修复） |
| N1 不存在工具名 | n1 | ✅ | 正确报 "Tool not found" |
| N2 空工具名 | n2 | ✅ | 修复 ArgumentException 暴露 → 静默跳过 |
| N3 非JSON参数 | n3 | ✅ | bash 报缺少必需参数 |
| N4 超大参数 | n4 | ✅ | bash 报 command 过长 |
| P4 空参数串 | p4 | ✅ | bash 报缺少必需参数 |
| P5 null参数 | p5 | ✅ | bash 报缺少必需参数 |
| P6 超长ID | p6 | ✅ | 200字符 tool_call_id 正常处理 |
| Q2 大写工具名 | q2 | ✅ | BASH not found（合理行为） |
| Q3 前后空格工具名 | q3 | ✅ | " bash " not found（合理行为） |
| Q4 重复ID | q4 | ✅ | 两个相同 id 工具调用都执行 |
| R1 ANSI输出 | r1 | ✅ | 安全拦截器触发 Path violation |
| R2 null字节 | r2 | ✅ | printf 含 \x00 正确处理 |
| R3 超长行 | r3 | ✅ | 5000字符无换行正确处理 |
| S1 --continue+工具 | s1→f1 --continue | ✅ | 恢复会话后工具调用正常 |
| S3 content+tool_call | s3 | ✅ | 文本和工具调用同时出现 |
| T1 点号工具名 | t1 | ✅ | read.file not found（合理行为） |
| T2 连字符工具名 | t2 | ✅ | read-file not found（合理行为） |
| T4 markdown结果 | t4 | ✅ | 工具输出 markdown 表格 |
| T5 json结果 | t5 | ✅ | 工具输出 JSON 格式 |
| U1 无finish_reason | u1 | ✅ | 流式 tool_call 后无 finish_reason 兜底刷出（修复） |
| U2 代码块结果 | u2 | ✅ | 工具输出代码块 |
| U4 嵌套json参数 | u4 | ✅ | 工具参数含嵌套 JSON |

**总计：46 场景全部通过，4 个 bug 修复**

## 修复的问题

### J1 超长响应注入超时（测试基础设施）
- **根因**: `BuildOpenAiStreamChunks` 逐字符生成 6000+ chunk，HTTP 注入超时
- **修复**: 改为按行发送，100 行只需 100 chunk
- **提交**: `7fe2219be`

### M6 JSON/NDJSON 字段名不一致（bug）
- **根因**: JSON 模式 `CliNonInteractiveResult.Response`（→ `response`），NDJSON 模式 `CliStreamEventData.Content`（→ `content`），字段名不一致
- **修复**: `CliNonInteractiveResult.Response` → `Content`，两种模式统一用 `content`
- **提交**: `4cd8ddca9`

### 模态误判 — ReadPdf 过宽关键词（bug）
- **根因**: `MediaIntentDetector` 的 `ReadPdf` 关键词包含 `"文件"`、`"file"`、`"文档"`、`"document"`，用户说"读取不存在的文件"就误判为 PDF 识别意图
- **修复**: 移除过宽关键词，仅保留 `"PDF"` 和 `"pdf"`
- **提交**: `2c662773b`

### N2 空工具名 ArgumentException 暴露（bug）
- **根因**: `QueryEngine.cs:425` 和 `ChatStreamChunkProcessor.cs:230` 只判 `name is null` 不判空字符串/空白，空名工具调用静默通过到 `IsToolAllowed` 抛 `ArgumentException`，异常消息暴露给用户
- **修复**: `name is null` → `string.IsNullOrWhiteSpace(name)`，空名工具调用静默跳过
- **提交**: `24b652dfd`

### U1 流式 tool_call 无 finish_reason 报空回复（bug）
- **根因**: `OpenAIQueryService.cs:184` 用 `finish_reason == "tool_calls"` 守门写入 `AllToolCalls`，[DONE] 时直接 `yield break` 不检查 `toolCallAccumulator`，无 finish_reason chunk → `AllToolCalls` 永不写入 → 下游报"模型返回空回复"
- **修复**: `await foreach` 循环结束后兜底检查 `toolCallAccumulator.Count > 0`，若有未刷出的 tool_call，构造带 `AllToolCalls` 的末尾 StreamEvent yield 出去
- **提交**: `323f08c12`

## 验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| responder IntentMatcher.cs | jcc 边缘场景测试 | ✅ | ✅ 手动 exe 验收 |

## responder 扩展的意图匹配

| 关键词 | 意图 | 响应类型 |
|--------|------|----------|
| f1/read file | 工具调用失败 | read 不存在文件 |
| f2/工具超时 | 工具执行慢 | bash sleep 10 |
| f3/多工具 | 多轮工具调用 | 2 轮 read |
| h4/emoji | Unicode 测试 | 多语言+emoji 文本 |
| i1/空工具 | 空工具调用 | tool_calls=[] |
| i2/多调用 | 多个工具调用 | 一次 2 个 tool_calls |
| i3/工具错误 | 工具执行错误 | bash exit 1 |
| j1/超长 | 超长响应 | 100 行文本 |
| k1/参数错误 | 参数格式错误 | JSON 缺引号 |
| k2/缺参数 | 参数缺失 | 空对象 {} |
| l4/流式中断 | 流式中断 | 无 [DONE] |
| l6/长输出 | 超长工具结果 | seq 1 500 |
| l11/特殊字符 | 参数特殊字符 | 引号/换行/制表 |
| l12/unicode参数 | 参数 Unicode | 中文/emoji |

---

## 2026-11-03 续测 — A/B/C 系列 + 代码质量 bug 修复

### 新增场景测试（60 场景）

#### A1-A20：工具参数边缘场景

| 场景 | 关键词 | 结果 | 说明 |
|------|--------|------|------|
| A1 大参数值100KB | a1 | ✅ | bash 报 command 过长，LLM 继续回应 |
| A2 名含换行 | a2 | ✅ | Tool 'bash\n' not found |
| A3 id含换行 | a3 | ✅ | ID 含换行不影响执行 |
| A4 null字节内容 | a4 | ✅ | null 字节被移除 |
| A5 纯CRLF | a5 | ✅ | 纯 CRLF 输出为空 |
| A6 首败次成 | a6 | ✅ | nonexistent_tool 失败 + bash 成功 |
| A7 键含空格 | a7 | ✅ | JSON 键含空格正常执行 |
| A8 大写BASH | a8 | ✅ | BASH not found（大小写敏感） |
| A9 嵌套引号 | a9 | ✅ | 多层嵌套引号正确处理 |
| A10 超长内容100KB | a10 | ✅ | yes+head 100KB 输出 |
| A11 纯数字id | a11 | ✅ | 纯数字 ID 正常处理 |
| A12 unicode id | a12 | ✅ | Unicode ID 正常处理 |
| A13 名含点号 | a13 | ✅ | bash.bash not found |
| A14 深层数组20 | a14 | ✅ | 20层嵌套数组参数（修复 responder 缺 Name） |
| A15 重复键 | a15 | ✅ | JSON 重复键容错 |
| A16 混合引号 | a16 | ✅ | 混合引号正确处理 |
| A17 参数含控制符 | a17 | ✅ | 制表符/回车/换行 |
| A18 空命令值 | a18 | ✅ | command 为空字符串报错 |
| A19 超长工具名255 | a19 | ✅ | 255字符工具名 not found |
| A20 JSON含注释 | a20 | ✅ | JSON 注释容错 |

#### B1-B20：命令行参数边缘场景

| 场景 | 结果 | 说明 |
|------|------|------|
| B1 空prompt | ✅ | exitCode=3 + ARG_PROMPT_EMPTY |
| B2 无效format | ✅ | exitCode=3 + 警告 |
| B3 未知参数 | ✅ | "Unknown option" 错误 |
| B4 无format | ✅ | 默认 text 格式 |
| B5 大写JSON | ✅ | format 大小写不敏感 |
| B6 混合大小写Json | ✅ | format 大小写不敏感 |
| B7 无prompt | ✅ | exitCode=3 + ARG_PROMPT_EMPTY |
| B8 空endpoint | ✅ | 回退到默认 endpoint |
| B9 无效endpoint | ✅ | "Invalid URI" 错误 |
| B10 空API key | ✅ | MCP 初始化超时 |
| B11 无效vendor | ✅ | CONFIG_INVALID_VALUE |
| B12 ndjson格式 | ✅ | NDJSON 输出正确 |
| B13 text格式 | ✅ | text 输出正确 |
| B14 xml格式 | ✅ | exitCode=3 + 警告 |
| B15 null字节prompt | ✅ | null 字节被忽略 |
| B16 Unicode prompt | ✅ | Unicode 正确保留 |
| B17 超长prompt 10KB | ✅ | 正常处理 |
| B18 无效permission mode | ✅ | 正常运行（默认行为） |
| B19 无效http mode | ✅ | 正常运行（默认行为） |
| B20 多个--prompt | ✅ | 使用最后一个值 |

#### C1-C20：工具执行结果边缘场景

| 场景 | 关键词 | 结果 | 说明 |
|------|--------|------|------|
| C1 exit code 1 | c1 | ✅ | 非零退出码 + LLM 继续回应 |
| C2 exit code 127 | c2 | ✅ | command not found |
| C3 exit code 255 | c3 | ✅ | Exit code 255 |
| C4 stdout+stderr | c4 | ✅ | 同时输出 stdout 和 stderr |
| C5 ANSI输出 | c5 | ✅ | ANSI 转义序列 |
| C6 JSON输出 | c6 | ✅ | JSON 格式数据 |
| C7 markdown输出 | c7 | ✅ | markdown 格式 |
| C8 代码块输出 | c8 | ✅ | 代码块 |
| C9 空输出 | c9 | ✅ | 空字符串 |
| C10 仅换行 | c10 | ✅ | 只有换行符 |
| C11 仅空格 | c11 | ✅ | 只有空格 |
| C12 嵌套JSON参数 | c12 | ✅ | 嵌套 JSON 对象 |
| C13 数组参数 | c13 | ✅ | JSON 数组 |
| C14 混合类型参数 | c14 | ✅ | 混合类型 |
| C15 并行一成一败 | c15 | ✅ | 一个成功一个失败 |
| C16 三并行全成 | c16 | ✅ | 三个并行成功 |
| C17 三并行全败 | c17 | ✅ | 三个并行失败 |
| C18 工具后最终回应 | c18 | ✅ | 工具调用后最终回应 |
| C19 工具链 | c19 | ✅ | 工具链两步 |
| C20 工具后空content | c20 | ✅ | 空 content 回应 |

**总计：60 新场景全部通过**

### 修复的 bug（18+ 个）

#### responder bug

| Bug | 根因 | 修复 | 提交 |
|-----|------|------|------|
| 子串匹配冲突 | `Contains("a1")` 匹配 "a10"-"a19" | `Contains("aX\n")` 利用换行边界 | `8e3f748e8` |
| A14 缺少 Name | `ToolCallFunctionDto` 缺 `Name="bash"` | 添加 Name | `8e3f748e8` |
| C1-C20 场景 | 新增工具执行结果边缘场景 | 添加到 IntentMatcher | `ff0b05f36` |

#### jcc 客户端 bug

| Bug | 根因 | 修复 | 提交 |
|-----|------|------|------|
| printf PathViolation | printf 未加入 SimpleReadOnlyCommands | 添加 printf | `44c79fdb5` |
| .NET 异常泄露 | GetString() 未检查 ValueKind | SafeGetString 宽容转换 | `7193dc2bf` |
| 空 tool_call_id | 空 ID 工具调用静默跳过 | 生成回退 ID | `c58154a5e` |
| RgSubCommand exitCode/ok 矛盾 | 搜索无结果 ok:true + exitCode:1 | Create(ok:false) | `b20e6e564` |

#### 代码质量 bug（GetString 未检查 ValueKind）

| Bug | 文件 | 修复 | 提交 |
|-----|------|------|------|
| Bug1 | OpenAIQueryService.cs:377 | 添加 ValueKind 检查 | `72351d6ec` |
| Bug1 同类 | ResponsesQueryService.cs:399 | 添加 ValueKind 检查 | `73458236e` |
| Bug1 同类 | AnthropicQueryService.cs:374 | 添加 ValueKind 检查 | `73458236e` |

#### 代码质量 bug（Unicode 代理对截断）

| Bug | 文件 | 修复 | 提交 |
|-----|------|------|------|
| Bug2 | ToolPatrolEngine.cs:171 | SafeTruncate 方法 | `72351d6ec` |
| Bug3 | OpenAIQueryService.cs:382 | 代理对边界检查 | `46b0fb7ff` |
| Bug3 同类 | ResponsesQueryService.cs:404 | 代理对边界检查 | `73458236e` |
| Bug3 同类 | AnthropicQueryService.cs:378 | 代理对边界检查 | `73458236e` |
| 代理对截断 | DagNodeSummarizer.cs:42 | 代理对边界检查 | `2e57228dc` |
| 代理对截断 | CtxInspectToolHandlers.cs:62 | 代理对边界检查 | `2e57228dc` |
| 代理对截断 | InformationEntropyGuardian.cs:265,276 | 代理对边界检查 | `e7911ce7e` |
| 23处统一修复 | 17个文件 | SafeTruncate 扩展方法 | `b44b8b5a8` |

### 验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| StringExtensions.SafeTruncate | 17个文件的字符串截断 | ✅ | ✅ 编译通过 |
| responder IntentMatcher A/W/X/Y/Z 匹配 | A1-A20 场景测试 | ✅ | ✅ 手动 exe 验收 |
| responder IntentMatcher C1-C20 | C1-C20 场景测试 | ✅ | ✅ 手动 exe 验收 |

---

## 2026-11-03 第三轮：D1-D20 流式响应边缘场景 + 流式处理 10 个 bug 修复

### D1-D20 场景测试结果

| 场景 | 关键词 | 结果 | 说明 |
|------|--------|------|------|
| D1 | role only | ✅ exit=1 | 只有 role chunk 无 content → 正确报空回复 |
| D2 | empty content between | ✅ exit=0 | 空 content chunk 正确跳过，输出 "helloworld" |
| D3 | usage first | ✅ exit=0 | usage 在首个 chunk 正确处理 |
| D4 | empty choices | ✅ exit=0 | 空 choices 数组正确跳过 |
| D5 | null delta | ✅ exit=0 | null delta 正确处理 |
| D6 | char by char | ✅ exit=0 | 逐字符流式（28+ chunk）正确累积 |
| D7 | interleaved | ✅ exit=0 | content 和 tool_call 交错 chunk 正确处理 |
| D8 | length finish | ✅ exit=0 | finish_reason=length 部分内容正确返回 |
| D9 | content filter | ✅ exit=1 | content_filter 无内容 → 正确报空回复 |
| D10 | done then extra | ✅ exit=0 | [DONE] 后额外数据正确忽略 |
| D11 | done whitespace | ✅ exit=0 | [DONE] 带尾随空格正确处理（问题8修复生效） |
| D12 | no done marker | ✅ exit=0 | 缺少 [DONE] 正确处理 |
| D13 | sse comment | ✅ exit=0 | SSE 注释行正确跳过 |
| D14 | extra fields | ✅ exit=0 | chunk 含额外字段正确忽略 |
| D15 | model change | ✅ exit=0 | 流中 model 名变化正确处理 |
| D16 | empty content chunks | ✅ exit=0 | 多个空 content chunk 正确跳过 |
| D17 | whitespace only | ✅ exit=0 | 纯空白 content 正确保留 |
| D18 | reasoning content | ✅ exit=0 | reasoning_content 正确流式输出 |
| D19 | multiple choices | ✅ exit=0 | 多 choices 只读 choices[0] |
| D20 | index gap | ✅ exit=0 | tool_call index 跳跃（0,2 缺 1）正确处理 |

### 流式处理 10 个 bug 修复

| 问题 | 严重度 | 位置 | 修复 | commit |
|------|--------|------|------|--------|
| 问题1 | 高 | ChatStreamChunkProcessor.cs:102 | IndexOutOfRange 崩溃 — 添加 Count==0 检查 | `7d190f7b3` |
| 问题2 | 中 | OpenAIQueryService.cs:93 | usage chunk FinishReason 硬编码 "stop" → 用 lastFinishReason | `449427ffb` |
| 问题3 | 中 | OpenAIQueryService.cs:199 | 只处理 "tool_calls" → 扩展到 "length"/"content_filter" | `449427ffb` |
| 问题4 | 中 | OpenAIQueryService.cs:236 | 兜底 flush FinishReason 硬编码 → 用 lastFinishReason | `449427ffb` |
| 问题5 | 高 | OpenAIQueryService.cs:157 | Index 缺失合并 — 提取 ProcessToolCallDelta 方法 | `7d190f7b3` |
| 问题6 | 中 | OpenAIQueryService.cs:159 | Id 覆盖重置 Arguments — ProcessToolCallDelta 修复 | `7d190f7b3` |
| 问题7 | 中 | OpenAIQueryService.cs:159 | Name 不更新 — ProcessToolCallDelta 修复 | `7d190f7b3` |
| 问题8 | 低 | OpenAIQueryService.cs:520 | [DONE] 不容忍尾随空格 → TrimEnd | `449427ffb` |
| 问题9 | 中 | QueryEngine.cs:428 | ToolName 未经过 RepairToolName — 添加修复 | `138b17cd6` |
| 问题10 | 高 | QueryEngine.cs:460 | length 截断时工具调用丢失 — 被问题3修复覆盖 | `449427ffb` |

### 验收表（追加）

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| ProcessToolCallDelta 方法 | OpenAIQueryService 流式 tool_call delta 处理 | ✅ | ✅ 编译通过 + LLM 609 测试通过 |
| lastFinishReason 跟踪 | usage chunk + 兜底 flush FinishReason | ✅ | ✅ D3/D8 场景验证 |
| [DONE] TrimEnd | OpenAIQueryService [DONE] 匹配 | ✅ | ✅ D11 场景验证 |
| RepairToolName in QueryEngine | QueryEngine 工具名修复 | ✅ | ✅ 编译通过 + Brain 951 测试通过 |
| responder D1-D20 场景 | IntentMatcher.StreamingEdge.cs | ✅ | ✅ 手动 exe 验收 |

---

## 2026-11-03 第四轮：E1-E20 工具调用边缘场景 + D9 content_filter 精准错误消息 + 第二流路径修复

### D9 content_filter 修复

| 根因 | 修复 | commit |
|------|------|--------|
| finish_reason 在事件管道中丢失 | 全链路透传：QueryEngine → QueryStreamChunk → AgentStreamChunk → AgentStreamChunkAdapter → ChatStreamEvent → QueryLoopMiddleware → SessionController | `d1ebdd526` |
| 第二流路径 4 个同类 bug | 硬编码 stop/inline delta/只检查 tool_calls/无兜底 flush | `d1ebdd526` |

D9 错误消息从 "模型返回空回复" 改为 "模型回复被安全过滤（content_filter）。请调整请求内容后重试，或切换模型/供应商。"

### E1-E20 场景测试结果

| 场景 | 关键词 | 结果 | 说明 |
|------|--------|------|------|
| E1 | parallel fail success | ✅ exit=0 | 并行调用一个成功一个失败，不影响最终结果 |
| E2 | retry after fail | ✅ exit=0 | 工具失败后 LLM 重试成功 |
| E3 | five parallel | ✅ exit=0 | 5 个并行工具调用全部成功 |
| E4 | chain depth 5 | ✅ exit=0 | 工具链 5 层深度全部完成 |
| E5 | missing quotes | ✅ exit=0 | 畸形 JSON（缺少引号）被修复管道正确修复 |
| E6 | trailing comma | ✅ exit=0 | 畸形 JSON（多余逗号）被修复管道正确修复 |
| E7 | single quote | ✅ exit=0 | 畸形 JSON（单引号）被修复管道正确修复 |
| E8 | json comment | ✅ exit=0 | 畸形 JSON（含注释）被修复管道正确修复 |
| E9 | 100 lines output | ✅ exit=0 | 工具输出 100 行正确处理 |
| E10 | json output tool | ✅ exit=0 | 工具输出 JSON 正确处理 |
| E11 | markdown table | ✅ exit=0 | 工具输出 markdown 表格正确处理 |
| E12 | code block output | ✅ exit=0 | 工具输出代码块正确处理 |
| E13 | empty history | ✅ exit=0 | 空对话历史首次消息正确处理 |
| E14 | long system prompt | ✅ exit=0 | 超长系统提示下工具调用正确处理 |
| E15 | emoji args | ✅ exit=0 | 工具参数包含 emoji 正确处理 |
| E16 | rtl text | ✅ exit=0 | 工具参数包含 RTL 文本正确处理 |
| E17 | very long id | ✅ exit=0 | 工具调用 ID 超长 1000 字符正确处理 |
| E18 | ten tool calls | ✅ exit=0 | 一轮 10 个工具调用全部成功 |
| E19 | null byte output | ✅ exit=0 | 工具输出包含 null 字节正确处理 |
| E20 | control chars | ✅ exit=0 | 工具输出包含控制字符正确处理 |

### 验收表（追加）

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| finish_reason 全链路透传 | SessionController 精准错误消息 | ✅ | ✅ D9 场景验证 |
| 第二流路径修复 | tool_description_request 路径 | ✅ | ✅ 编译通过 + LLM 609 测试通过 |
| responder E1-E20 场景 | IntentMatcher.EdgeE.cs | ✅ | ✅ 手动 exe 验收 |

---

## 2026-11-03 第五轮：F1-F20 工具调用高级边缘场景

### F1-F20 场景测试结果

| 场景 | 关键词 | 结果 | 说明 |
|------|--------|------|------|
| F1 | 10kb single line | ✅ exit=0 | 工具结果含 10KB 单行正确处理 |
| F2 | 1000 lines | ✅ exit=0 | 工具结果含 1000 行正确处理 |
| F3 | crlf output | ✅ exit=0 | 工具结果含 CRLF Windows 换行正确处理 |
| F4 | bom output | ✅ exit=0 | 工具结果含 UTF-8 BOM 头正确处理 |
| F5 | nested json 5 | ✅ exit=0 | 工具参数含嵌套 JSON 5 层正确处理 |
| F6 | 100 keys args | ✅ exit=0 | 工具参数含 100 个键正确处理 |
| F7 | all json types | ✅ exit=0 | 工具参数含所有 JSON 类型正确处理 |
| F8 | 3 rounds 3 tools | ✅ exit=0 | 一轮 3 个工具调用全部成功 |
| F9 | empty then tool | ✅ exit=0 | 空 content 后工具调用正确处理 |
| F10 | tool name digits | ✅ exit=0 | 工具名含数字（bash123）正确处理 |
| F11 | tool name underscore | ✅ exit=0 | 工具名含下划线正确处理 |
| F12 | tool name mixed case | ✅ exit=0 | 工具名混合大小写（Bash）正确处理 |
| F13 | 4 byte emoji args | ✅ exit=0 | 工具参数含 4 字节 emoji（补充平面）正确处理 |
| F14 | ansi color output | ✅ exit=0 | 工具结果含 ANSI 颜色码正确处理 |
| F15 | terminal control | ✅ exit=0 | 工具结果含终端控制码正确处理 |
| F16 | chinese args | ✅ exit=0 | 工具参数含中文正确处理 |
| F17 | japanese args | ✅ exit=0 | 工具参数含日文正确处理 |
| F18 | korean args | �.exit=0 | 工具参数含韩文正确处理 |
| F19 | cyrillic args | ✅ exit=0 | 工具参数含西里尔文正确处理 |
| F20 | mixed script | ✅ exit=0 | 工具参数含混合脚本正确处理 |

### 验收表（追加）

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| responder F1-F20 场景 | IntentMatcher.EdgeF.cs | ✅ | ✅ 手动 exe 验收 |

---

## 2026-11-03 第六轮：G1-G20 工具调用参数边缘场景

### G1-G20 场景测试结果

| 场景 | 关键词 | 结果 | 说明 |
|------|--------|------|------|
| G1 | empty arguments | ✅ exit=0 | 空 arguments {} 正确处理 |
| G2 | no arguments field | ✅ exit=0 | 无 arguments 字段（null）正确处理 |
| G3 | duplicate tool id | ✅ exit=0 | 重复 tool_call_id 正确处理 |
| G4 | very long tool name | ✅ exit=0 | 超长工具名 255 字符正确处理 |
| G5 | special char id | ✅ exit=0 | ID 含特殊字符 @#$%^&*() 正确处理 |
| G6 | empty model field | ✅ exit=0 | 响应缺 model 字段正确处理 |
| G7 | extra metadata | ✅ exit=0 | 响应含额外 metadata 正确处理 |
| G8 | usage every chunk | ✅ exit=0 | usage 在每个 chunk 正确处理 |
| G9 | cached tokens | ✅ exit=0 | cached_tokens 在 usage 正确处理 |
| G10 | reasoning non stream | ✅ exit=0 | reasoning_content 非流式正确处理 |
| G11 | multi choices non stream | ✅ exit=0 | 多 choices 非流式正确处理 |
| G12 | markdown link output | ✅ exit=0 | 工具结果含 markdown 链接正确处理 |
| G13 | html entities | ✅ exit=0 | 工具结果含 HTML 实体正确处理 |
| G14 | unicode escape | ✅ exit=0 | 工具结果含 Unicode 转义正确处理 |
| G15 | repeated lines | ✅ exit=0 | 工具结果含重复行 100 行正确处理 |
| G16 | progressive output | ✅ exit=0 | 工具结果含渐进式输出正确处理 |
| G17 | tool then text | ✅ exit=0 | 工具调用后纯文本无工具正确处理 |
| G18 | 5 round tool chain | ✅ exit=0 | 5 轮工具调用链全部完成 |
| G19 | nested array 3 | ✅ exit=0 | 工具参数含嵌套数组 3 层正确处理 |
| G20 | null value args | ✅ exit=0 | 工具参数含 null 值正确处理 |

### 验收表（追加）

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| responder G1-G20 场景 | IntentMatcher.EdgeG.cs | ✅ | ✅ 手动 exe 验收 |

---

## 2026-11-03 第七轮：H1-H20 纯内容流式边缘场景

### H1-H20 场景测试结果

| 场景 | 关键词 | 结果 | 说明 |
|------|--------|------|------|
| H1 | single line text | ✅ exit=0 | 纯文本 1 行正确返回 |
| H2 | 100 lines text | ✅ exit=0 | 纯文本 100 行正确返回 |
| H3 | code block text | ✅ exit=0 | 纯文本含代码块正确返回 |
| H4 | table text | ✅ exit=0 | 纯文本含 markdown 表格正确返回 |
| H5 | link text | ✅ exit=0 | 纯文本含 markdown 链接正确返回 |
| H6 | emoji text | ✅ exit=0 | 纯文本含 emoji 正确返回 |
| H7 | 4byte emoji text | ✅ exit=0 | 纯文本含 4 字节 emoji 正确返回 |
| H8 | chinese text | ✅ exit=0 | 纯文本含中文正确返回 |
| H9 | japanese text | ✅ exit=0 | 纯文本含日文正确返回 |
| H10 | korean text | ✅ exit=0 | 纯文本含韩文正确返回 |
| H11 | cyrillic text | ✅ exit=0 | 纯文本含西里尔文正确返回 |
| H12 | rtl text content | ✅ exit=0 | 纯文本含 RTL 文本正确返回 |
| H13 | mixed script text | ✅ exit=0 | 纯文本含混合脚本正确返回 |
| H14 | 100kb text | ✅ exit=0 | 纯文本 100KB 正确返回 |
| H15 | json in text | ✅ exit=0 | 纯文本含 JSON 正确返回 |
| H16 | xml in text | ✅ exit=0 | 纯文本含 XML 正确返回 |
| H17 | html in text | ✅ exit=0 | 纯文本含 HTML 正确返回 |
| H18 | sql in text | ✅ exit=0 | 纯文本含 SQL 正确返回 |
| H19 | url in text | ✅ exit=0 | 纯文本含 URL 正确返回 |
| H20 | control chars text | ✅ exit=0 | 纯文本含控制字符正确返回 |

### 验收表（追加）

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| responder H1-H20 场景 | IntentMatcher.EdgeH.cs | ✅ | ✅ 手动 exe 验收 |

## I1-I20: 错误恢复与安全边缘场景（2026-11-03）

### 场景列表

| 编号 | 描述 | 结果 | 说明 |
|------|------|------|------|
| I1 | fail then retry | ✅ exit=0 | 工具失败后 LLM 重试成功 |
| I2 | 3 consecutive fails | ✅ exit=0 | 连续 3 次工具失败后正确终止 |
| I3 | slow command | ✅ exit=0 | sleep 2 慢命令正确执行 |
| I4 | exit code 1 | ✅ exit=0 | bash exit 1 正确处理 |
| I5 | exit code 127 | ✅ exit=0 | command_not_found 正确处理 |
| I6 | exit code 255 | ✅ exit=0 | bash exit 255 正确处理 |
| I7 | large stderr | ✅ exit=0 | 大量 stderr 输出正确处理 |
| I8 | stdout stderr mix | ✅ exit=0 | stdout+stderr 混合正确处理 |
| I9 | nonexistent tool name | ✅ exit=0 | 不存在工具名正确报错 |
| I10 | missing required field | ✅ exit=0 | 参数缺失必需字段正确处理 |
| I11 | wrong param type | ✅ exit=0 | 参数类型错误正确处理 |
| I12 | empty command | ✅ exit=0 | 空命令正确处理 |
| I13 | whitespace command | ✅ exit=0 | 纯空白命令正确处理 |
| I14 | injection attempt | ✅ exit=0 | 注入尝试安全处理 |
| I15 | pipe command | ✅ exit=0 | 管道命令正确执行 |
| I16 | redirect command | ✅ exit=0 | 重定向命令正确执行 |
| I17 | background command | ✅ exit=0 | 后台命令正确处理 |
| I18 | subshell command | ✅ exit=0 | 子shell 命令正确执行 |
| I19 | variable command | ✅ exit=0 | 变量命令正确执行 |
| I20 | wildcard command | ✅ exit=0 | 通配符命令正确处理 |

### 匹配冲突修复

**根因**: 旧 I1/I2/I3 场景用 `Contains("i1")` 等子串匹配，抢先匹配新 I1-I20 场景的 `"i1\n..."` 提示词。同理 F1/F2/F3、H4、C1/C2/C3 也有子串冲突。

**修复**:
- 旧 I1/I2/I3: `Contains("i1")` → `Contains("i_empty\n")` / `Contains("i_multi\n")` / `Contains("i_error\n")`
- 旧 F1/F2/F3: `Contains("f1")` → `Contains("f1_read\n")` / `Contains("f2_timeout\n")` / `Contains("f3_multi\n")`
- 旧 H4: `Contains("h4")` → `Contains("h4_unicode\n")`
- C1/C2/C3: `Contains("exit code 1")` → `Contains("c1 exit code 1")` (加 c 前缀)
- I4/I5/I6: `Contains("exit code 1")` → `Contains("i4 exit code 1")` (加 i 前缀, 避免子串匹配 "exit code 127")

**commit**: `1d4a65dda`

### 验收表（追加）

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| responder I1-I20 场景 | IntentMatcher.EdgeI.cs | ✅ | ✅ 手动 exe 验收 |
| 匹配冲突修复 | IntentMatcher.cs + EdgeI.cs | ✅ | ✅ I1-I20 全部通过 |

## 不舒服点修复（2026-11-03）

### 修复清单

| # | 不舒服点 | 根因 | 修复 | commit |
|---|---------|------|------|--------|
| 1 | jcc 启动卡 30 秒 | ModelListFetcher 遍历全部 7 个 profile 并行拉取, JCC_ENDPOINT 只覆盖一个, 其余指向 api.openai.com | 新增 JCC_SKIP_MODEL_FETCH=1 环境变量跳过 model fetch | `18e182aba` |
| 2 | 错误消息泄露用户路径 | hint 包含绝对路径 C:\Users\XXX\.jcc\runtime\jcc_error.log | XdgPathResolver.ShortenPath 将主目录替换为 ~ | `18e182aba` |
| 3 | 非交互模式提示按 Esc | "请按 Esc 打断后重试" 在 --non-interactive 模式无意义 | 移除 Esc 提示, 改为通用 "请重试" | `a01011af2` |
| 4 | responder lastSeenIndex 不重置 | MockServer clear 后 responder lastSeenIndex 保持高值, 新请求 index 从 0 被过滤 | 检测请求列表为空或全部 index <= lastSeenIndex 时重置 | `183bf4aac` |

### 回归测试

| 域 | 测试数 | 结果 |
|----|--------|------|
| Host | 1516 | ✅ 全通过 |
| LLM | 609 | ✅ 全通过 |
| Agents | 626 | ✅ 全通过 |
| Hands.Shell | 526 | ✅ 全通过 |
| Brain.Prompts | 212 | ✅ 全通过 |
| Brain.Context | 950 | ✅ 全通过 |
| Guard.Security | 1136 | ✅ 全通过 |
| Guard.Config | 1306 | ✅ 全通过 |
| McpToolDispatch | 523 | ✅ 全通过 |
| Mcp | 1023 | ✅ 全通过 |
| **总计** | **8427** | **0 失败** |

## 全量回归测试（2026-11-03）

### 测试范围

220 场景全量回归，分批执行（每 40 场景重启 MockServer + responder，避免服务状态污染）。

| 批次 | 场景 | 数量 | 结果 | 耗时 |
|------|------|------|------|------|
| 1 | A-F | 120 | ✅ 全通过 | 698s |
| 2 | G-H | 40 | ✅ 全通过 | 228s |
| 3 | I + SX + SY | 60 | ✅ 全通过 | 392s |
| **总计** | **A-I + SX + SY** | **220** | **0 失败** | **1318s** |

### 场景系列清单

| 系列 | 场景数 | 说明 |
|------|--------|------|
| A1-A20 | 20 | 基础工具调用 |
| B1-B20 | 20 | 参数处理 |
| C1-C20 | 20 | 错误恢复 |
| D1-D20 | 20 | 流式响应边缘场景 |
| E1-E20 | 20 | 工具调用边缘场景 |
| F1-F20 | 20 | 工具调用高级边缘场景 |
| G1-G20 | 20 | 工具调用参数边缘场景 |
| H1-H20 | 20 | 纯内容流式边缘场景 |
| I1-I20 | 20 | 错误恢复与安全边缘场景 |
| SX1-SX20 | 20 | 流式协议边缘场景 |
| SY1-SY20 | 20 | 工具调用处理边缘场景 |

### 发现：服务状态污染

长时间运行 MockServer + responder（>100 场景）后出现状态污染导致假失败。根因：MockServer Interactive 模式累积请求队列 + responder lastSeenIndex 跟踪不同步。解决方案：分批测试，每 40 场景重启服务。

## 第五轮：连接失败无限重试修复（2026-11-03）

### Bug：连接失败时 jcc 无限重试卡死 60 秒+

**根因**：三层重试叠加 + 无限重试配置

| 层 | 问题 | 修复 |
|----|------|------|
| ResilientHttpExecutor | `TransientErrorPredicate = null` + `TotalBudget = 24h` → `IsRetryAllowed` 总返回 true → 无限重试 | 默认模式也设置 `TransientErrorPredicate = IsTransientCapacityError`，只有 429/529 无限重试 |
| StreamingFallbackDecorator | `HttpRequestException` 无条件触发 fallback → 连接失败后非流式又重试 | 连接级错误（无 StatusCode）不 fallback |
| QueryEngine | `IsRetryable` 对 `HttpRequestException` 返回 true → 再重试 3 次 | 连接级错误不重试（ResilientHttpExecutor 已重试） |
| SocketsHttpHandler | 未设 `ConnectTimeout` → 连接超时默认 15 秒 | `ConnectTimeout = 5 秒` |

**修复效果**：连接失败从无限卡死（60 秒+超时）→ 35 秒返回错误（重试 3 次后放弃）

**commit**: `a18b7eb3a`

### SZ1-SZ20: 工具执行边缘场景

20 个工具执行边缘场景（超长输出、ANSI 转义、JSON、markdown、多语言等），全部通过。
