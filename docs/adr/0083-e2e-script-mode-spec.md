# 0083. E2E 测试脚本模式规范

- 状态：accepted
- 日期：2026-09-08
- 决策者：项目架构组

## 背景

Interactive 模式下 `Console.In.ReadLineAsync` 从重定向 stdin 管道读取存在竞争条件，偶发卡死60s超时。单轮命令尤其容易触发。通过将 `Mode` 设计为计算属性，从架构层面消除模式误用。

> ADR: [0021](0021-e2e-script-mode-inferred.md)（Mode 计算属性）

## 详细内容

### 架构防护：Mode 为计算属性（不可手动设置）

`ConversationScript.Mode` 是**只读计算属性**，根据 `Turns.Count` 自动推断：

```csharp
public ConversationMode Mode => Turns.Count == 1
    ? ConversationMode.NonInteractive   // 单轮 → NonInteractive
    : ConversationMode.Interactive;     // 多轮 → Interactive
```

**开发者无需（也无法）手动设置 Mode**。删除所有 `Mode = ConversationMode.xxx` 赋值，约束编码进类型系统，从架构层面消除模式误用。

### 推断规则

| 脚本类型 | 自动推断为 | 原因 |
|----------|-----------|------|
| **单轮(Turns.Count==1)** | `NonInteractive` | `-p` 参数直接传命令，不经过 stdin 管道，无竞争条件 |
| **多轮(Turns.Count>1)** | `Interactive` | 需要根据上一轮输出发送下一轮输入，无法用 `-p` |

### 运行时不变量断言

`DualRoleConversationRunner.ValidateScriptMode` 和 `CoverageTestBase.ValidateScriptMode` 在运行时断言计算属性推断正确：
- 单轮 + 非 NonInteractive → `[GEN036]` 报错
- 多轮 + 非 Interactive → `[GEN037]` 报错

### 新增 E2E 脚本时的检查清单

1. **不要设置 Mode** — 它是计算属性，赋值会编译失败
2. 单轮命令 → 只写1个 Turn，Mode 自动推断为 NonInteractive
3. 多轮交互 → 写多个 Turn，Mode 自动推断为 Interactive
4. 运行测试确认无 `[GEN036]`/`[GEN037]` 报错

### 定位 E2E 卡死问题的快速方法

1. **先用 `jcc.exe -p "/命令"` 非交互模式验证命令本身** — 不需要 MockServer，1秒出结果
2. **用 `dotnet test --filter "单个测试"` 本地跑** — E2E 框架自己管 MockServer
3. **检查 jcc.exe 时间戳** — E2E 用 `Host.Tests\Debug` 路径，改代码后必须重编译 Host.Tests
4. **同步 `ReadToEnd` 读 stderr** — `BeginErrorReadLine` 在进程被Kill时不flush

## 替代方案

无。计算属性方案将模式约束编码进类型系统，比运行时检查更安全。
