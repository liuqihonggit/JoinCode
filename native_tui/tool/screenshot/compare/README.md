# L2 截图校准 — 像素级比对

ANSI 采集帧 → cell 网格 → 同字体栅格化 → 逐像素 diff。

## 流程（三步）

```powershell
# 1. TS 参考帧采集（首次或基准变更后；需 Node 24+）
cd native_tui/tool/screenshot/reference
npm ci
node build.mjs D:/project/claude-code-rust/claude-code-rev-main/src
$env:FORCE_COLOR='3'
node reference.mjs

# 2. C# 采集帧（native_tui 自研渲染管线，80×24 固定场景）
dotnet run --project native_tui/apps/NativeTui.Demo -- --capture .xxx/screen.ansi

# 3. 像素比对（需 Python 3 + Pillow）
python native_tui/tool/screenshot/compare/compare_reference.py `
  --reference native_tui/tool/screenshot/reference `
  --capture .xxx/screen.ansi `
  --output .xxx/visual
```

退出码 = 是否存在像素差异（0 = 全部一致）。输出 `{name}_reference.png` / `{name}_csharp.png` / `{name}_diff.png` + `report.json`（differentCharacters / differentPixels）。

## region 声明

验证 Clawd 9×3 与输入框边框 80×3 两个区域（region 硬编码于脚本），不代表真实终端截图或整屏像素一致。输入状态、模态、工具、其他宽度须另验。默认字体 Cascadia Mono 20，同一字体将双方 ANSI 单元栅格化，消除字体差异后的残差即渲染差异。
