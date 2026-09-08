# 0092. mcp_call 跨 shell 引号宽容 + 工具名模糊匹配

- 状态：proposed
- 日期：2026-09-08
- 决策者：用户 + AI

## 背景

AI 用 PowerShell 调用 `jcc.exe mcp_call agent '{"prompt":"echo hello"}'` 时,PowerShell 5.1 会剥掉单引号内 JSON 的双引号,导致 jcc.exe 收到 `{prompt:echo hello}`(裸对象)。

当前 `ToolCallRepairService.FixUnquotedValues`(`ToolCallRepairService.cs:329-402`)遇到空格就停止收集值字符(第 366 行 `!char.IsWhiteSpace(json[i])`),对 `{prompt:echo hello}` 只收集 `"echo"`,遇空格停止,判定不可修复 → 整体修复失败。

同时,工具名错误(如 `agent_launch`)时只提示"用 jcc mcp list 查看",不做模糊匹配推荐 `agent`。`mcp_schema` 输出也无跨 shell 调用示例,AI 不知道如何正确传参。

### 复现

```powershell
& $jccExe mcp_call agent_launch '{"prompt":"echo hello","enableWorktreeIsolation":true}'
# 收到: {prompt:echo hello,enableWorktreeIsolation:true}
# 报错: JSON 解析失败: 'p' is an invalid start of a property name
# 修复提示: JSON repair failed. Original: {prompt:echo hello,...}
```

根因链:
1. PowerShell 引号剥落(shell 侧,无法改)
2. `FixUnquotedValues` 不处理带空格值(jcc 侧,可改)
3. 工具名 `agent_launch` 无模糊建议(jcc 侧,可改)

## 决策

4 项改进,按收益排序:

### 1. 增强 `FixUnquotedValues` 处理带空格的未加引号值(最高收益)

遇到 `:value` 且 value 含空格时,收集到下一个 `,`/`}`/`]` 为止整体加引号。需先排除 `true`/`false`/`null`/数字/嵌套对象 `{`/数组 `[`/已加引号 `"`。

**策略**:先尝试"到空格停"的保守收集(现有逻辑),如果保守收集后后面不是 `,`/`}`/`]`,则改为"到 `,`/`}`/`]` 停"的激进收集并加引号。

这一项让 jcc 对 PowerShell 引号剥落**免疫**,AI 用任何 shell 都能成功调用。

### 2. 工具名模糊匹配建议

`McpCommand.cs:23` 找不到工具时,新增 `SuggestToolNames(toolName, registry)`:
- 前缀匹配:`agent_launch` 以 `agent` 开头 → 建议 `agent`
- 子串匹配:`launch_agent` contains `agent` → 建议
- 编辑距离:Levenshtein ≤ 3 的优先
- 输出:`未找到工具: agent_launch。是否想用: agent, agent_list, agent_status?`

工具名用枚举 `.ToValue()` 保证标准名(去硬编码),MCP/自定义工具走 registry 原名。

### 3. `mcp_schema` 附带跨 shell 调用示例

`ExecuteSchemaAsync` 输出末尾追加:
```
调用示例:
  PowerShell: jcc mcp_call <tool> --% "{\"key\":\"value\"}"
  Bash:       jcc mcp_call <tool> '{"key":"value"}'
  Cmd:        jcc mcp_call <tool> "{\"key\":\"value\"}"
```

### 4. JSON 解析失败时给可执行修正写法

`ParseArgs` 修复失败分支,检测输入特征(有 `:` 但无 `"`、Windows 环境)追加:
```
提示: 输入看起来像被 shell 剥掉了引号。
  PowerShell: 用 --% 停止解析,或用 \" 转义双引号
  示例: jcc mcp_call <tool> --% "{\"prompt\":\"echo hello\"}"
```

## 替代方案

- **只做第 1 项**:最小改动,但工具名错误时 AI 仍要 `mcp_list` 自己找,体验差
- **要求用户用 `--%`**:把责任推给用户/AI,不宽容;且 AGENTS.md 已要求"用移动代替删除""架构最优",宽容机制更符合项目哲学
- **不改,只更新文档**:不解决问题,AI 仍会踩坑

## 后果

- 正面:AI 在任何 shell(PowerShell/Bash/Cmd)都能成功调用 `mcp_call`,引号剥落自动修复;工具名拼错有建议,schema 有示例,降低 AI 调用失败率
- 负面:`FixUnquotedValues` 激进收集可能误判(如 `{a:1 b:2}` 无逗号分隔的非法 JSON 会被修复成 `{"a":"1 b:2"}`),但这类输入本就非法,修复后至少能解析
- 中性:宽容度提高意味着对"错误输入"的容忍度提高,可能掩盖用户真实错误;但有 `RepairHint` 日志可追溯

## TDD 记划

| 任务 | 红测试 | 修复 | 绿测试 |
|------|--------|------|--------|
| 1 | `RepairJson_UnquotedValueWithSpaces_AddsQuotes` | `FixUnquotedValues` 激进收集 | 同红测试转绿 |
| 2 | `SuggestToolNames_AgentLaunch_SuggestsAgent` | 新增 `SuggestToolNames` | 同红测试转绿 |
| 3 | `ExecuteSchema_IncludesShellExamples` | `ExecuteSchemaAsync` 追加示例 | 同红测试转绿 |
| 4 | `ParseArgs_StrippedQuotes_SuggestsEscape` | `ParseArgs` 失败分支追加提示 | 同红测试转绿 |
