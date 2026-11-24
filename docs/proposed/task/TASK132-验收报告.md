# TASK132 验收报告 — 轮次闸门状态机（自查）

- 验收时间：2026-11-24
- 验收对象：`round_gate` 状态机 + 按需注入（commit `c2c6c9f3f` 及其后的验收补充）
- 验收结论：**通过（含 3 项明确未覆盖）**
- 验收人：AI 自查（非用户终验；文案与推荐体感仍需用户验收）

## 1. 与上一轮"验收"的差别

上一轮只有样例单测 + A/B/C/D 四条 mock 剧本，属于"复述我的预期"。本次补了四类**能自我证伪**的手段：

| 手段 | 作用 | 结果 |
|------|------|------|
| 不变量穷举 | 不再逐条写样例，改为"任意 ≤4 步选择序列都必须满足 7 条不变量" | 约 95 万状态机实例全通过（`RoundGateInvariantTests`） |
| 变异自检 | 故意改坏生产代码，验证测试会红 | 6/6 变异全部被抓住（`non_deliverables_tools/scripts/verify/round_gate_mutation_check.py`） |
| 真机出口补齐 | 之前只验 1/3/4/5；补 0/6/9 三条外部链路 | E/F/G 均 `EXIT=0`，F 真实打出"9 降级交付被拒" |
| 体积基线 | "瘦身"给数字而不给感觉 | 常驻 ~736 → ~381 token（-48%） |

## 2. 不变量穷举

文件：`modules/cognition/brain_other_tests/query/gate/RoundGateInvariantTests.cs`

序列空间：11 个选项 × {有证据, 无证据}；`3 自验` 再 × {none, pass, fail} → 26 种步型；
深度 1~4 全排列 × {严格, 非严格} ≈ 95 万个会话实例。

| 编号 | 不变量（从需求独立推出，不读实现） |
|------|----------------------------------|
| I1 | 中间态（`1/2/3/7/8`）永不被放行收尾 |
| I2 | `5 完成` 放行 ⇒ 最近一次作废动作（`1/2/7/8`）之后存在 `3 + pass` |
| I3 | 熔断生效时，最后选择不是强制项 ⇒ 必须拦截 |
| I4 | `9 降级` 放行 ⇒ 最近一次"移交/作废"动作是 `4 用户验收` |
| I5 | 严格模式下 `5 完成` 放行 ⇒ 同上必须有已移交的 `4` |
| I6 | `ReplanCount` 等于序列中 `7` 的实际出现次数（计数器单源） |
| I7 | `SelfAcceptPassed` 与序列推断一致（最近一次 `3` 为 pass 且之后无作废动作） |

结果：`AnySequenceUpToFourSteps_HoldsInvariants` 通过；`DisabledGate_NeverBlocks` 通过。

## 3. 变异自检（测试是否真的有效）

脚本对生产代码做单点替换 → 跑 Gate 测试 → 无论结果都按原字节还原；还原后全量复跑必须绿。

| 变异 | 改法 | 期望 | 实际 |
|------|------|------|------|
| M1 自验判断恒真 | `SelfAcceptPassed = ...Pass` → `= true` | 红 | 红 ✅ |
| M2 空转计数失效 | `IdleStreak++` → `+= 0` | 红 | 红 ✅ |
| M3 去掉报错调用守卫 | 删 `!toolResult.IsError &&` | 红 | 红 ✅ |
| M4 关掉完成需自验拦截 | `if (!SelfAcceptPassed)` → `if (false)` | 红 | 红 ✅ |
| M5 关掉熔断绕过拦截 | 熔断判定 → `if (false)` | 红 | 红 ✅ |
| M6 推荐改回迁移前计算 | `recommendation = Recommend()` → `= baseline` | 红 | 红 ✅ |

6/6 被抓；还原后 Gate 全量测试 `exit=0`。

## 4. 真机链路（真 `jcc.exe` + OpenAI.MockServer）

| 场景 | 覆盖 | 结果 | 日志 |
|------|------|------|------|
| A | `1` → `3` 缺 verdict 报错 → 拦截 → `3 pass` → `5` | `EXIT=0`，`GATE132_FINAL_OK` | `.xxx/logs/gate132_a_slim3.log` |
| B | 无自验直选 `5`，`MAX_REJECT=2` 触顶 | `EXIT=0`，`5 完成被拒` + 强制结束 | `.xxx/logs/gate132_b_slim3.log` |
| C | 严格模式两次推荐 `4` 后按推荐收尾 | `EXIT=0`，`GATE132C_FINAL_OK` | `.xxx/logs/gate132_c_slim3.log` |
| D | 严格模式 `3 pass` 后直选 `5` 连续被拦 | `EXIT=0`，`严格模式：完成前必须先选 4` | `.xxx/logs/gate132_d_slim3.log` |
| E（新增） | `0 阻塞上报` 出口 | `EXIT=0`，`GATE132E_FINAL_OK` | `.xxx/logs/gate132_e.log` |
| F（新增） | `9` 未移交被拒 → `4` → `9` 放行 | `EXIT=0`，真实打出`9 降级交付被拒` | `.xxx/logs/gate132_f.log` |
| G（新增） | `6 等候后台` 出口 | `EXIT=0`，`GATE132G_FINAL_OK` | `.xxx/logs/gate132_g.log` |

> F 的第一版剧本没打出拦截：带工具调用的回合不会触发收尾裁决。加一个纯文本"想停"回合后分支才被真正覆盖——这条记下来，避免以后用"有工具调用的回合"假验收尾拦截。

## 5. 常驻提示词体积基线

统计脚本 `non_deliverables_tools/scripts/verify/round_gate_token_baseline.py`（字符分类粗估：中文≈1 token/字，ASCII≈4 字符/token）。

| 项 | 旧（`873067674`） | 新（`c2c6c9f3f`） | 变化 |
|----|------------------|------------------|------|
| 系统 Section | 957 字符 ≈ 541 token | 521 字符 ≈ 282 token | -48% |
| 工具描述 | 779 字符 ≈ 195 token | 396 字符 ≈ 99 token | -49% |
| 常驻合计 | ≈ 736 token | ≈ 381 token | -48% |

选项细则不再常驻：只随所选 `choice` 注入单段（约 50 token/次）。

## 6. 全量回归

```bash
dotnet test modules/cognition/brain_other_tests/Brain.Other.Tests.csproj   # 591 通过
dotnet test modules/mcp/mcp_tests/Mcp.Tests.csproj                        # 431 通过
dotnet test modules/cognition/brain_prompts_tests/Brain.Prompts.Tests.csproj # 229 通过（前一轮）
pwsh non_deliverables_tools/scripts/Build-AllDomains.ps1 -Configuration Debug # 全仓全通过
```

## 7. 明确未覆盖（不得当作"已验证"）

1. **真实模型行为**：所有真机剧本都是脚本化 LLM，只能证明"给定选择时链路正确"，不能证明真实模型会按选项语义选择。
2. **跨回合熔断**：`RoundGateSession` 生命周期 = 单次查询，用户回合之间计数清零；"连续 2 回合空转"这类跨回合语义没有实现也未测。
3. **出口组合**：`0/6/9` 各只验一条正常路径；`7` 触发熔断→强制 `10`、`8` 驳回计数→强制 `7` 只有单测与不变量覆盖，没有真机剧本。
4. **文案可读性**：Section 精简后是否仍能让模型理解，只能用户终验（真实 TUI 会话体感）。

## 8. 复跑方式

```powershell
# 不变量 + 全量 Gate
dotnet test modules\cognition\brain_other_tests\Brain.Other.Tests.csproj --filter "FullyQualifiedName~Core.Tests.Query.Gate"
# 变异自检（会临时改源码，自动还原）
python -u non_deliverables_tools\scripts\verify\round_gate_mutation_check.py
# 体积基线
python -u non_deliverables_tools\scripts\verify\round_gate_token_baseline.py
# 真机（示例 E）
artifacts\bin\OpenAI.MockServer\Debug\net10.0\JoinCode.OpenAI.MockServer.exe --port 18136 --config .xxx\gate132_e_mockserver.json
JCC_ROUND_GATE=on JCC_ENDPOINT=http://localhost:18136 OPENAI_API_KEY=sk-test-dummy JCC_VENDOR=openai JCC_MODEL_ID=gpt-4o artifacts\bin\JoinCode\Debug\net10.0\jcc.exe --trust --await 90 -p "请推进任务"
```
