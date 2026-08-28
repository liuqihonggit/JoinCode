# 0021. E2E 脚本 Mode 计算属性

- 状态：accepted
- 日期：2026-08-29
- 决策者：项目架构组

## 背景

Interactive 模式下 `Console.In.ReadLineAsync` 从重定向 stdin 管道读取存在竞争条件，偶发卡死 60s 超时。单轮命令尤其容易触发。早期脚本需手动设置 Mode，易误用。

## 决策

**`ConversationScript.Mode` 是只读计算属性，根据 `Turns.Count` 自动推断**：

```csharp
public ConversationMode Mode => Turns.Count == 1
    ? ConversationMode.NonInteractive   // 单轮 → NonInteractive
    : ConversationMode.Interactive;     // 多轮 → Interactive
```

**开发者无需（也无法）手动设置 Mode**。删除所有 `Mode = ConversationMode.xxx` 赋值，约束编码进类型系统，从架构层面消除模式误用。

**运行时不变量断言**：`DualRoleConversationRunner.ValidateScriptMode` 和 `CoverageTestBase.ValidateScriptMode` 在运行时断言计算属性推断正确：
- 单轮 + 非 NonInteractive → `[GEN036]` 报错
- 多轮 + 非 Interactive → `[GEN037]` 报错

## 替代方案

1. **手动设置 Mode**：放弃。开发者可能误设（如单轮设为 Interactive），触发 stdin 竞争卡死。
2. **运行时自动检测 stdin 是否为管道**：放弃。检测不可靠，且 TTY/管道判断跨平台不一致。
3. **统一用 NonInteractive**：放弃。多轮交互需根据上一轮输出发送下一轮输入，无法用 `-p` 参数。

## 后果

- 正面：模式误用从架构层消除；运行时断言兜底
- 负面：单轮/多轮的区分硬编码为 `Turns.Count == 1`，未来若有第三种模式需重构
- 中性：新增 E2E 脚本时不设置 Mode，只写 Turn，自动推断
