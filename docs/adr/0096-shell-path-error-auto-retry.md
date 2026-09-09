# 0096. Shell 路径错误自动重试

- 状态：accepted
- 日期：2026-09-09
- 决策者：项目架构组

## 背景

LLM 生成的 shell 命令常出现路径分隔符混用(如 `cat D:\project\src/file.txt`),在 bash/PowerShell 执行时因路径解析失败而报错。用户需求:shell 工具执行失败(路径错误)时自动转换路径分隔符重试一次,成功则不报错给 LLM(透明)。

此前路径大小写守卫(ADR 0039)和统一路径归一化工具(ADR 0095)已落地,但都是"执行前拦截/归一化"。本 ADR 补"执行后失败自动重试"这一环,形成完整的路径防御链:
- 执行前:PathCaseSensitiveGuard 拦截大小写不敏感误删(ADR 0039)
- 执行后:ShellPathRetryHelper 路径错误自动重试(本 ADR)

## 决策

新建 `ShellPathRetryHelper` 静态工具(Tools.Shell),两个纯函数:

1. `IsPathError(ToolResult?)` — 检测 `IsError + 输出含路径错误关键词`(中英文 14 个关键词:No such file/not found/cannot find/系统找不到指定的路径/...)
2. `TryNormalizeCommand(string, bool toForwardSlash)` — 仅当命令同时含 `\` 和 `/` 时统一为目标分隔符,返回新命令或 null

接入 `ShellToolHandlers.ShellExecuteAsync`(bash,统一为 `/`)和 `PowerShellToolHandlers.PowerShellAsync`(PowerShell,统一为 `\`):
- 管道执行后检查结果
- 若 `IsPathError(result)` 且 `TryNormalizeCommand` 返回非 null,重新构造 ShellPipelineContext 执行一次
- 重试成功(非 IsError)→ 返回重试结果(透明,LLM 不感知失败)
- 重试失败 → 返回原始结果(不掩盖原始错误)

## 替代方案(考虑过但放弃)

1. **静态全项目修正所有路径** — 85 处 Replace 散落全项目,逐一修正风险高、收益低(运行时自动重试已覆盖)
2. **命令解析层提取路径 token 精确归一化** — ShellCommand.ReferencedPaths 不含裸参数(无分隔符/尾斜杠),解析层改造范围大
3. **配置开关控制是否重试** — 用户明确"默认重试",配置开关留后续(开心路径先硬编码)

## 现状

- ShellPathRetryHelper 22 单元测试通过(IsPathError 8 + TryNormalizeCommand 14)
- Shell 全量 255 测试通过,无回归
- bash + PowerShell 双接入

## 约束

- 只重试一次(避免无限循环)
- 只在"路径错误"时触发(不干扰其他错误如权限拒绝、超时)
- 只在"混合分隔符"时归一化(不破坏纯 `\` 或纯 `/` 的命令)
- 重试成功对 LLM 透明(不报原始失败),重试失败返回原始结果(不掩盖原始错误)
