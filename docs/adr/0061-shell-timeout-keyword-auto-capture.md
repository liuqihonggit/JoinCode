# 0061. 脚本超时关键字自动捕获机制

- 状态：accepted
- 日期：2026-09-04
- 决策者：AI + 用户确认

## 背景

AI 执行含 `Start-Sleep -Seconds 60`、`sleep 60`、`timeout /t 60` 等等待关键字的脚本时，默认 120s 绝对超时可能不足（若脚本还有其他耗时操作），或 AI 传入的 timeout 参数小于脚本内等待时间，导致命令被强制终止。AI 收到超时错误但不知道根因是参数冲突，体验差。

原有 `ShellSearchTimeoutMiddleware` 仅对搜索命令（rg/grep/find）缩短超时，不解析脚本内容中的等待关键字。

## 决策

1. 新增 `ShellTimeoutKeywordExtractor`（纯静态类）：解析脚本命令中的等待关键字，提取最大等待时间（秒）。支持 PowerShell（Start-Sleep）、Bash（sleep）、cmd（timeout /t、ping -n）、Python 内嵌（time.sleep）、C# 内嵌（Thread.Sleep）。

2. 新增 `ShellTimeoutKeywordMiddleware`（Shell 管道中间件，位于 AbsoluteTimeoutMiddleware 之前）：
   - 检测到等待关键字时，计算 `requiredTimeout = waitSeconds + bufferSeconds`（默认缓冲 30s）
   - 若用户未显式传入 timeout 且 requiredTimeout > 当前有效超时 → 自动延长 OverrideTimeout
   - 若用户显式传入 timeout 且 timeout < requiredTimeout → **直接返回 Error 给 AI**（不抛异常给软件），提示增大 timeout 或移除等待关键字

3. 修改 `AbsoluteTimeoutMiddleware`：尊重 OverrideTimeout，若 OverrideTimeout > 基准上限则采用。使关键字延长能覆盖绝对超时截断。

4. 新增配置项 `ShellExecutionConfig.TimeoutKeywordBufferSeconds`（默认 30s）。

## 替代方案

- **自动延长超时并警告（不报错）**：更宽容但可能掩盖 AI 的参数错误。用户选择直接报错让 AI 显式修正参数。
- **修改全局 config.AbsoluteTimeoutSeconds**：影响所有命令，非按命令调整。已否决。
- **仅检测 PowerShell**：覆盖面窄。用户选择全 shell + cmd + 内嵌脚本。

## 后果

- 正面：脚本含 wait/sleep 时不再被默认超时误杀；参数冲突直接报错给 AI，AI 可自行修正；自动延长无需用户干预。
- 负面：正则解析有性能开销（每次命令执行都解析）；复杂脚本（如外部脚本文件内的 sleep）无法检测。
- 中性：OverrideTimeout 优先级链变为 OverrideTimeout > Timeout > config > policy。
