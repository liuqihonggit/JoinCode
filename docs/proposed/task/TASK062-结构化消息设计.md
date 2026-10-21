# TASK062 — 结构化消息（消息 ID / 思考时长 / CoT 可展开 / 回复）

> 状态：implemented（2026-10-21 三决策点用户确认全部按推荐后落地；待真机验收思考模型场景）
> 前置：三缺陷修复（8378ae808）完结；用户指令："加入消息格式：消息 id，思考时间，CoT 可视化可点击查看详情，回复"

## 现状差距（调研）

| 层 | 现状 | 缺口 |
|----|------|------|
| BridgeEvent.AssistantMessage | Text + Markdown 两字段 | 无 MessageId / ThinkingDurationMs / ThinkingContent |
| InProcessEventConsumer | OnThinking 只发 started/ended 信号（思考内容丢弃） | CoT 内容不达 TUI |
| ThinkingEnded(DurationMs) | 时长已有 | 未挂到消息上（消费者两处状态拼接复杂） |
| AssistantMessageItem | ● + 正文 | 无思考前缀行、无展开交互 |
| 交互 | Ctrl+O 无绑定（diff 预览展开是 TODO 注释） | CoT 展开键位空缺 |

## 设计方案（对齐基准 Claude Code 形态）

### 渲染形态（目标视觉）

```
❯ 你是谁
  ⎿ 思考 3.2s · ctrl+o 展开推理            ← dim 行（有思考时；无思考不渲染）
● 我是由智谱 AI 训练的 GLM 大模型…          ← 回复正文（现状不变）
```

展开态（Ctrl+O 切换最近一条消息）：

```
  ⎿ 思考 3.2s ▾                              ← 展开标记
    用户在询问我的身份，我应该介绍 GLM 系列…   ← CoT 全文 dim 缩进（2 空格）
● 我是由智谱 AI 训练的 GLM 大模型…
```

### 数据链（四字段向后兼容扩展）

1. **BridgeEvent.AssistantMessage** 末尾加可选参数：
   `int MessageId = 0`（回合序号，消费者递增）、`int ThinkingDurationMs = 0`、`string? ThinkingContent = null`
   —— 旧调用零破坏（默认值），与 SessionInfo.Vendor/LoggedIn 同先例
2. **InProcessEventConsumer**：OnThinking 累积思考文本到 `_thinkingContent`；OnDone 组装
   AssistantMessage 时带上 MessageId（interlocked 自增）+ ThinkingDurationMs（首思考墙钟差，已有数据）+ ThinkingContent
3. **AssistantMessageItem**：加三个 init 字段；`MeasureHeight` 有思考 +1 行；渲染思考前缀行
   （`⎿ 思考 {s} · ctrl+o 展开推理` dim）；`IsExpanded` 状态 + Toggle（Ctrl+O）控制 CoT 全文渲染（dim 2 空格缩进）
4. **ReplShell**：Ctrl+O 绑定 — 找最后一条带 ThinkingContent 的 AssistantMessageItem/MarkdownMessageItem 调 Toggle；刷新帧
5. **bridge 子进程协议**（JSON）：字段同名透传（JsonSerializer 默认含新字段，旧 TUI 读新引擎忽略未知字段兼容）

### MessageId 语义

- 进程内：`InProcessEventConsumer` 构造时起始值注入（EngineTurnClient 每会话一个 consumer → 1,2,3…递增）
- 用途：Ctrl+O 定位 + transcript 落盘对账（后续扩展）；**非时间戳**（终端 UI 不显示 ID 本身——视觉噪音，按基准）

### 交互键位（对齐基准 Claude Code）

| 键 | 行为 |
|----|------|
| Ctrl+O | 切换**最近一条**消息的思考块展开/收起（有 CoT 才响应） |

### 不做（本期边界）

- 每条消息独立 ID 显示（视觉噪音，基准无此形态——消息按时间序即可定位）
- 思考过程实时逐字流（引擎 OnThinking 有内容，但基准也是完成后折叠呈现——保持）
- 多条消息同时展开（基准单条聚焦语义）

## 影响面清单

| 文件 | 改动 |
|------|------|
| native_tui/src/NativeTui.Core/bridge/BridgeEvent.cs | AssistantMessage +3 可选字段 |
| native_tui/apps/NativeTui.Host/InProcessEventConsumer.cs | 思考累积 + MessageId 自增 + OnDone 组装 |
| native_tui/src/NativeTui.Core/repl/MessageItems.cs | AssistantMessageItem +3 字段 + 思考行渲染 + 展开 |
| native_tui/src/NativeTui.Core/repl/MarkdownMessageItem.cs | 同上（Markdown 路径同构） |
| native_tui/src/NativeTui.Core/repl/ReplShell.cs | Ctrl+O 键位绑定 |
| native_tui/src/NativeTui.Core/bridge/ReplBridgeDriver.cs | 事件新字段透传到消息项 |
| 引擎侧 BridgeEventConsumer（bridge 协议） | 同字段透传（保持两宿主一致） |

## 验收表（实现后填）

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| AssistantMessage 3 字段链 | driver → 消息项 | ✅ 8 测试 | 待真机（思考模型） |
| 思考前缀行渲染 | AssistantMessageItem/Markdown | ✅ | 待真机 |
| Ctrl+O 展开交互 | ReplShell 键位 | ✅ 2 测试 | 待真机 |

## 决策点（需用户确认）

1. **思考行位置**：回复上方独立 dim 行（本设计）vs 正文内嵌折叠头（基准 Claude Code 2024 形态）？
2. **Ctrl+O 作用域**：最近一条（本设计，基准语义）vs 光标选中消息（需结合消息光标导航）？
3. **无思考模型**（sensenova 非思考路径）：完全无思考行（本设计）vs 显示"思考 0s"？

## 实现基线（2026-10-21）

- NativeTui.Tests：809 → **817**（+8：数据链 2 + 渲染 3 + Ctrl+O 2 + 兼容 1）
- Host.Tests：62 → **64**（+2：结构化组装 + 序号跨回合递增）
- 探针实证：id=1 thinkMs=0 cotLen=0（sensenova 非思考路径字段零值正确）
- 多段思考时长累计修复（OnThinking 每段重启计时 — 测试锁定 1500+1500）
- CoT 行数守卫 200 上限（刷屏防护）

<!-- 🤖 Auto Decision: 2026-10-21 -->
<!-- 决策: 三决策点按用户确认（独立dim思考行/Ctrl+O最近一条/无思考不渲染）；ToggleLastThinking 用引用相等索引比较取两类消息更靠后者 -->
<!-- 原因: 基准 Claude Code 2025 形态 + 用户三连确认 -->
<!-- 替代方案: 光标选中作用域（需结合消息光标导航，后续增强） -->
<!-- 验证: 817+64 全绿 + 探针字段实证 ✅ -->
