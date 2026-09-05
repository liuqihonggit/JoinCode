# 全量 MCP 工具验证计划

> 总计 387 个工具，48 个分类
> 验证方法：`jcc mcp call <tool> --args '<json>' --json`
> 状态标记：✅ OK（空参数通过）| ⚠️ PARAM（需参数，非 bug）| ❌ BUG（工具本身有错误）

## 验证进度

| 分类 | 工具数 | ✅ OK | ⚠️ PARAM | ❌ BUG | 状态 |
|------|--------|-------|----------|--------|------|
| Agent | 14 | 4 | 10 | 0 | ✅ 完成 |
| analytics | 12 | | | | ⏳ 待验证 |
| chat | 3 | | | | ⏳ 待验证 |
| code | 2 | | | | ⏳ 待验证 |
| code_analysis | 4 | | | | ⏳ 待验证 |
| code_execution | 3 | | | | ⏳ 待验证 |
| code_generation | 3 | | | | ⏳ 待验证 |
| code_index | 20 | | | | ⏳ 待验证 |
| config | 3 | | | | ⏳ 待验证 |
| context | 2 | | | | ⏳ 待验证 |
| cron | 4 | | | | ⏳ 待验证 |
| desktop | 32 | | | | ⏳ 待验证 |
| error_recovery | 7 | | | | ⏳ 待验证 |
| execution | 17 | | | | ⏳ 待验证 |
| file | 13 | | | | ⏳ 待验证 |
| github | 30 | | | | ✅ 已验证(29/29 gh工具) |
| goal | 3 | | | | ⏳ 待验证 |
| graph | 16 | | | | ⏳ 待验证 |
| interaction | 1 | | | | ⏳ 待验证 |
| lsp | 10 | | | | ⏳ 待验证 |
| memory | 13 | | | | ⏳ 待验证 |
| messaging | 1 | | | | ⏳ 待验证 |
| mcp | 22 | | | | ⏳ 待验证 |
| mode | 2 | | | | ⏳ 待验证 |
| network | 1 | | | | ⏳ 待验证 |
| notebook | 10 | | | | ⏳ 待验证 |
| notification | 1 | | | | ⏳ 待验证 |
| permission | 7 | | | | ⏳ 待验证 |
| plan | 10 | | | | ⏳ 待验证 |
| planning | 1 | | | | ⏳ 待验证 |
| policy | 2 | | | | ⏳ 待验证 |
| sandbox | 6 | | | | ⏳ 待验证 |
| search | 8 | | | | ⏳ 待验证 |
| skill | 25 | | | | ⏳ 待验证 |
| structured_output | 2 | | | | ⏳ 待验证 |
| system | 4 | | | | ⏳ 待验证 |
| task | 12 | | | | ⏳ 待验证 |
| team | 10 | | | | ⏳ 待验证 |
| terminal | 1 | | | | ⏳ 待验证 |
| todo | 4 | | | | ⏳ 待验证 |
| tool_creation | 3 | | | | ⏳ 待验证 |
| tool_debug | 3 | | | | ⏳ 待验证 |
| trigger | 1 | | | | ⏳ 待验证 |
| vision | 13 | | | | ⏳ 待验证 |
| voice | 4 | | | | ⏳ 待验证 |
| web | 5 | | | | ⏳ 待验证 |
| worktree | 8 | | | | ⏳ 待验证 |
| git | 9 | | | | ⏳ 待验证 |

## 详细结果

### Agent (14 个)
- ✅ `agent_list` — 返回可用 agent 类型列表
- ⚠️ `verification_agent` — 需参数: code
- ⚠️ `Agent` — 需参数: description
- ⚠️ `explore_agent` — 需参数: target_path
- ⚠️ `forward_user_input` — 需参数: agent_id
- ⚠️ `plan_agent` — 需参数: goal
- ⚠️ `SendMessage` — 需参数: to
- ✅ `agent_running` — 返回正在运行的代理(0个)
- ⚠️ `guide_agent` — 需参数: question
- ⚠️ `agent_get_messages` — 需参数: agent_id
- ✅ `list_agents` — 返回可用内置 Agent 列表
- ⚠️ `agent_status` — 需参数: agent_id
- ⚠️ `agent_stop` — 需参数: agent_id
- ⚠️ `general_agent` — 需参数: task
