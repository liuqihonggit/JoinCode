# TS 参考区域渲染

固定基准：claude-code-rust 的 working 分支提交 4b87a363fd206c1154969e3914dcf1c1ca272f2e。

在本目录运行（Node.js 24+）：

```powershell
npm ci
node build.mjs C:/path/to/claude-code-rev-main/src
$env:FORCE_COLOR='3'
node reference.mjs
```

Clawd 直接编译原组件；桥接文件只替换 Ink 导出、终端环境和主题色。Prompt 使用原 PromptInput 的边框属性建立隔离边界样本（round、left=false、right=false、bottom=true、width=100%），未启动原应用的完整输入状态机。

在本工程根目录采集 C# 同一场景（native_tui 自研渲染管线）：

```powershell
dotnet run --project apps/NativeTui.Demo -- --capture .xxx/screen.ansi
python tool/screenshot/compare/compare_reference.py --reference tool/screenshot/reference --capture .xxx/screen.ansi --output .xxx/visual
```

Python 依赖 Pillow。比对细节见 `../compare/README.md`。
