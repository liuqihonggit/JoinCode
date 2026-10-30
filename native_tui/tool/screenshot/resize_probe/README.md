# L3 resize 感知探针 — 物理终端视角的多尺寸转储

与 L2 像素比对（`compare_reference.py`，进程内 80×24 固定单帧）互补：本工具回答
**"拖拽终端窗口调整大小之后，banner/输入框到底落在视口第几行"** — 此前该问题
只能靠人眼拖窗口观察，现在可脚本化、可重复、可留存证据。

## 原理

```
目标 exe（jcc / NativeTui.Demo）
   │  写 VT 流
   ▼
ConPTY（pywinpty）──── resize 相位序列：120x30 → 80x24 → 140x40 …
   │  下游字节流 = Windows Terminal 所见的同一流（含 conhost reflow 重排）
   ▼
stream.bin（原始素材留存）
   │  按相位偏移全量重放
   ▼
pyte 独立 VT 解释器 → 可见网格 + 标记行定位 + 光标位 → report.md + *.txt + *.png
```

- **ConPTY 下游流**即真实终端收到的流 — conhost 在 resize 时对缓冲区做 reflow 重排，
  下游收到的是重排后的全帧，pyte 只负责忠实解码，不模拟 reflow。
- **pyte**（独立开源 VT 解释器，tmux capture-pane 同类物）= 独立于 AI/用户的第三方证据源。
- 每相位 = 字节流 `[0, offset_i)` 在全新 Screen 上重放 — 自包含、确定性、同一输入永远同一网格。

## 用法

```powershell
pip install pyte pywinpty          # --png 另需 Pillow

# Demo（SimulatedEngine 确定性场景，渲染链路 = ReplShell 与 jcc 同源）
python native_tui/tool/screenshot/resize_probe/resize_probe.py `
  --target native_tui/artifacts/bin/NativeTui.Demo/Debug/net10.0/NativeTui.Demo.exe `
  --phases "120x30@1200,80x24@1200,140x40@1200" `
  --out .xxx/resize_probe/demo --png

# 真实 jcc.exe（需引擎环境；裸 ConPTY 下停在"引擎启动中"属预期 — 引擎未起）
python native_tui/tool/screenshot/resize_probe/resize_probe.py `
  --target artifacts/bin/JoinCode/Debug/net10.0/jcc.exe `
  --phases "120x30@4000,80x24@1500" --out .xxx/resize_probe/jcc
```

- `--phases`：`WxH@settle_ms` 逗号分隔；首相位 = 启动尺寸，其后每相位执行一次
  `ResizePseudoConsole` + 静置后快照字节偏移。
- `--mark`：定位标记正则（可重复；缺省 `jcc v` / `❯` / `查看快捷键`）。
- 输出：`report.md`（跨相位标记行汇总表 + 各相位网格）、`NN_WxH.txt/.png`、
  `stream.bin`（原始流，可离线重放分析）、`phases.json`（偏移索引）。

## 已产出的证据（2026-10-31，TASK076）

**修复前**（demo_seq — InlineComposer 相对锚定 fullReset）：

| 相位 | 尺寸 | banner `jcc v` 行 | 判定 |
|---|---|---|---|
| 0 | 120×30 | 0 | ✅ 钉在顶部 |
| 1 | 80×24（缩窗） | **4** | ❌ 顶部 4 行空白，banner 被推下 — 用户投诉复现 |
| 2 | 140×40（放窗） | 0 | ✅（CUU 钳制侥幸归位） |
| 3 | 90×20（再缩） | 0 | ✅（侥幸 — 帧对齐未被 reflow 破坏） |

**修复后**（demo_fixed — 方案 A 基准对齐：fullReset 改 2J+3J+光标归位 绝对锚定 + 整帧重绘）：
四相位 banner 恒在第 0 行，布局逐行一致。差异分析与 TDD 验收记录见
[TASK076](../../docs/proposed/task/TASK076-TUI-resize-fullReset-锚定语义差异-四问深度版.md)。
