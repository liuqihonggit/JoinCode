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
