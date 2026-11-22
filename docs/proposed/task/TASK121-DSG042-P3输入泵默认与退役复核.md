# TASK121 — DSG042-P3 RawStdinPump 切默认退役复核（TASK103 #52）

> 日期：2026-11-23
> 来源：TASK103 未清事项总账 #52；TASK075 #43；DSG042 §3 P3 / §执行状态；TASK066 板块 B5/B6
> 裁决方式：无人值守，按一手源码与提交事实取证

## 一、原文诉求

DSG042 P3 原文：

> 双通道灰度全键位对齐验证（含 Windows Terminal/conhost 双终端实测）→ 默认切 RawStdinPump → TryCollectPaste 状态机 + ConsoleKeyAdapter 归一路径归档 `.xxx/`。
> 验收：键位兼容矩阵（ReadKey 通道 14 键位 vs Raw 通道全集）逐项打勾。

TASK103 #52 转述为「RawStdinPump 切默认退役（双终端矩阵跑齐后）」。

## 二、一手证据

### 2.1 「切默认」已发生，且不是本轮才发生

`native_tui/apps/NativeTui.Host/Program.cs:315-324`：

```csharp
// TASK077 — 输入泵平台分流（首帧必需 — 接收用户输入）
IInputPump? inputPump;
if (useRawStdinPump || OperatingSystem.IsWindows())
{
    inputPump = new RawStdinPump(terminalQuerier);
}
else
{
    inputPump = null;
}
```

提交 `f8b07f5b9`（Windows 鼠标点击支持）已把原先 `useRawStdinPump ? new RawStdinPump(...) : null`
改为 Windows 默认 Raw；提交 `864e7870b`（TASK078 首帧优化）在重构启动顺序时原样保留该分支。
因此：

- Windows 交互模式默认就是 `RawStdinPump`，`--raw-input` 在 Windows 上已是冗余开关；
- DSG042/TASK066 里「默认仍是 ReadKey，等矩阵后切」的描述是施工前的计划，不是当前事实。

### 2.2 原文预设的「默认 ReadKeyPump」已被两次架构演进取代

`native_tui/src/NativeTui.Core/repl/ReplShell.cs:729`：

```csharp
_inputPump = inputPump ?? new Win32InputPump();
```

宿主在 Windows 注入 RawStdinPump；未注入时的回退已是 `Win32InputPump`（TASK077/078 鼠标链路），
不是 DSG042 设想的 `ReadKeyPump`。DSG042 的「双通道 ReadKey ↔ Raw 互斥」模型已经过期。

### 2.3 ReadKeyPump 与 TryCollectPaste 是零引用死代码

全仓检索：

```text
new ReadKeyPump      → 0 命中
ReadKeyPump(         → 0 构造命中
TryCollectPaste      → 仅 ReadKeyPump.cs 私有方法
```

`ReadKeyPump` 既无生产构造点，也无测试构造点；它携带的 bracketed paste 重组状态机随类一起死亡。
归档它不改变运行期行为。

### 2.4 ConsoleKeyAdapter 不能按原文归档

原文要求「ConsoleKeyAdapter 归一路径归档」，但一手代码有三个活消费点：

| 位置 | 用途 |
|------|------|
| `native_tui/src/NativeTui.Core/repl/input/Win32InputPump.cs:75` | Win32 `KEY_EVENT` → `KeyPress`（默认回退泵） |
| `native_tui/src/NativeTui.Core/repl/input/Win32ConsoleInputPump.cs:93` | `INPUT_RECORD` → `KeyPress`（TASK077 泵） |
| `native_tui/src/NativeTui.Core/repl/ReplShell.cs:1421` | 外部注入 `ConsoleKeyInfo` 的统一归一入口 |

因此「归档 ConsoleKeyAdapter」已被 TASK077/078 架构取代；照做会打断鼠标/注入键链路。
这是 DSG042 P3 原文失效的部分，不是未完成的欠账。

### 2.5 双终端矩阵仍无证据

文档全仓检索只找到计划与待办：

- `DSG042-tui-raw-stdin-input-pump-migration.md:93`：P3 仍标 `⬜ 未做`；
- `TASK066-TUI对齐ClaudeCode-终端集成与遗留收口.md:216-217`：B5/B6 仍标 `⏸ 待用户真机验收`；
- `TASK066...:330`：B6 手动验收项「两终端逐项键位表」无回填。

`--selftest-input`（`Program.cs:825-871`）是管道字节流探针，覆盖 UTF-8 增量解码、前缀攒齐、
paste 整块、修饰组合共 9 键断言；它证明 Raw 全链解析，不覆盖 Windows Terminal 与 conhost
的终端层差异，不能冒充双终端矩阵。

### 2.6 相关文档存在与代码相反的表述

`TASK077-ReadConsoleInput-pump-reconstruct.md:6-8` 的状态澄清写「Windows 默认 Win32ConsoleInputPump，
Linux 仍走 RawStdinPump」，与 `Program.cs:317` 相反。该文档用于说明 TASK102 的历史复核，
本轮同步纠正为代码事实，避免后续再用它做裁决依据。

## 三、裁决

| 原文子项 | 裁决 | 依据 |
|----------|------|------|
| 默认切 RawStdinPump | ✅ 已完成（非本轮） | `Program.cs:317` + `f8b07f5b9`/`864e7870b` |
| ReadKeyPump / TryCollectPaste 退役 | ✅ 本轮归档 | 零构造点，零行为变化 |
| ConsoleKeyAdapter 归档 | ❌ 不适用（架构演进取代） | 三处活消费点，归档会断链 |
| 双终端矩阵逐项打勾 | ⏸ 残余人工验收 | 无 WT/conhost 实测记录；本环境只能跑管道探针 |

结论：#52 按「已完成可执行部分 + 架构分歧纠正 + 人工残余登记」闭合，
不再把「切默认」当作待施工事项；真正的残余是人工终端矩阵，不伪造证据、不擅自宣称通过。

## 四、本轮施工

1. 归档 `native_tui/src/NativeTui.Core/repl/input/ReadKeyPump.cs` → `.xxx/ReadKeyPump.cs.<时间戳>.del`；
2. 修正源码注释中指向已归档类的 `<see cref="ReadKeyPump"/>`，避免文档注释引用失效；
3. 回填 DSG042 执行状态、TASK066 B5/B6 状态、TASK077 状态澄清；
4. 总账 #52 划线并写明残余人工项。

## 五、验收口径

- `dotnet build native_tui/src/NativeTui.Core/NativeTui.Core.csproj -c Debug --no-restore -m:1`；
- `dotnet build native_tui/tests/NativeTui.Tests/NativeTui.Tests.csproj -c Debug --no-restore -m:1`；
- 定向测试：`RawStdinPumpTests`、`Win32ConsoleInputPumpTests`、`ConsoleKeyAdapterTests`；
- 全量 `NativeTui.Tests`（已知 RawStdinPumpTests 偶发 flaky，需单跑复核）；
- `jcc --selftest-input`（管道字节流探针，明确不等同双终端矩阵）；
- `jcc --selftest-tui` 冒烟；
- 源码树检索 `new ReadKeyPump` = 0；`ConsoleKeyAdapter` 三个消费点仍在。

## 六、教训

1. 「默认通道已切」与「矩阵验收已过」是两件事，前者可从代码证明，后者只能从人工记录证明；
2. 设计文档中的「退役 A/B」清单要用当时的消费图复核，架构演进后 B 可能仍活着，照单删会断链；
3. 归档零引用类比改活链安全，但必须先证明零构造点，不能只凭注释里写「现役」就留着。
