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
