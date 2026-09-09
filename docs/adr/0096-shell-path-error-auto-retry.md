# 0096. Shell 路径处理策略:去掉执行前自动转换 + 执行后失败重试

- 状态：accepted
- 日期：2026-09-09
- 决策者：项目架构组

## 背景

LLM 生成的 shell 命令常出现路径分隔符混用(如 `cat D:\project\src/file.txt`),在 bash/PowerShell 执行时因路径解析失败而报错。

此前存在两层路径处理:
- **执行前**:ShellPathGateMiddleware 自动转换 WorkingDirectory 和 Command 中的路径格式(Windows↔POSIX)
- **执行后**:无任何重试

问题:ShellPathGateMiddleware 的自动转换造成了 `D:\a\b\c/d` 混合分隔符问题(转换逻辑只转了一部分),且自动修正可能改错路径(如把正则 `\b` 误当路径分隔符)。用户哲学:**报错让 LLM 自己修正路径参数,自动修正反而会出事**。

## 决策

### 1. 去掉 ShellPathGateMiddleware 的执行前自动路径转换

`ShellPathGateMiddleware.InvokeAsync` 不再调用 `GatePath` / `GateCommandPaths`,只保留 UNC 路径警告(只读不改) + next 调用。命令和工作目录原样传递给执行器,路径错误由执行器报错反馈给 LLM,LLM 根据报错自己修正路径参数。

### 2. 保留 ShellPathRetryHelper 执行后失败重试(兜底)

新建 `ShellPathRetryHelper` 静态工具(Tools.Shell),两个纯函数:

1. `IsPathError(ToolResult?)` — 检测 `IsError + 输出含路径错误关键词`(中英文 14 个关键词)
2. `TryNormalizeCommand(string, bool toForwardSlash)` — 仅当命令同时含 `\` 和 `/` 时统一为目标分隔符,返回新命令或 null

接入 `ShellToolHandlers.ShellExecuteAsync`(bash,统一为 `/`)和 `PowerShellToolHandlers.PowerShellAsync`(PowerShell,统一为 `\`):
- 管道执行后检查结果
- 若 `IsPathError(result)` 且 `TryNormalizeCommand` 返回非 null,重新构造 ShellPipelineContext 执行一次
- 重试成功(非 IsError)→ 返回重试结果(透明,LLM 不感知失败)
- 重试失败 → 返回原始结果(不掩盖原始错误)

## 替代方案(考虑过但放弃)

1. **静态全项目修正所有路径** — 85 处 Replace 散落全项目,逐一修正风险高、收益低
2. **命令解析层提取路径 token 精确归一化** — ShellCommand.ReferencedPaths 不含裸参数(无分隔符/尾斜杠),解析层改造范围大
3. **配置开关控制是否重试** — 用户明确"默认重试",配置开关留后续(开心路径先硬编码)
4. **保留 ShellPathGateMiddleware 正则保护型转换** — 虽然正则只匹配盘符开头路径不会误伤 `\b`,但转换逻辑本身会造成混合分隔符(`D:\a\b\c/d`),且违背"报错让 LLM 自己修正"哲学

## 现状

- ShellPathGateMiddleware 路径转换已去掉,只保留 UNC 警告 + next
- ShellPathRetryHelper 22 单元测试通过
- ShellPathGateMiddlewareTests 更新为验证"不转换"行为
- Shell 全量 256 测试通过,无回归
- bash + PowerShell 双接入重试

## 约束

- 只重试一次(避免无限循环)
- 只在"路径错误"时触发(不干扰其他错误如权限拒绝、超时)
- 只在"混合分隔符"时归一化(不破坏纯 `\` 或纯 `/` 的命令)
- 重试成功对 LLM 透明(不报原始失败),重试失败返回原始结果(不掩盖原始错误)
- 执行前不自动转换路径(报错让 LLM 自己修正)
