# dsh_parity — DSH 像素对齐参考物捕获 + 对比校验工具

> 非交付品工具（non_deliverables_tools）。用途：把 DSH（DeepSeek Harness）Web UI 捕获为
> **可机读参考物**（截图 + DOM 盒树 + 设计 token），并对比本项目 WebUI 的实际渲染帧（L3 像素 diff）。
>
> 设计文档: [DSG042](../../modules/apps/docs/design/DSG042-DSH像素级对齐对比校验体系.md)
> 任务清单: [TASK053](../../modules/apps/docs/task/TASK053-DSH像素对齐对比校验体系任务清单.md) · [TASK054](../../modules/apps/docs/task/TASK054-WebView2自研WebUI-spike任务清单.md)
> ADR: [0138](../../docs/proposed/adr/architecture/0138-gui-webview2-webui-rendering.md)（WebView2 + 自研 WebUI 路线）

## 目录结构

```
capture/   DSH 参考物捕获（CDP + 系统 Edge）
generate/  token 生成器（dsh-tokens.json → tokens.css）
compare/   本项目渲染帧截图 + 帧 diff（热力图/网格归因）
lib/       公共模块（Edge 探测 / 像素采样 / 区域放大）
reference/ golden 参考物（入库）      dumps/ 瞬时产物（gitignored）
```

## 产出物（写入 `reference/`）

| 文件 | 内容 |
|------|------|
| `dsh-web-{W}x{H}.png` | 固定视口截图（DPR=1，深色主题） |
| `dsh-dom-{W}x{H}.json` | 可见元素盒树：box/背景色/字号/圆角/padding/gap/稳定路径 |
| `dsh-tokens.json` | 全部 `--dsw-*`/`--ds-*` CSS 变量，双主题解析值 |
| `dsh-meta.json` | 版本锚定（dsh 版本/视口/DPR/时间） |

## 运行

前置：Node ≥ 20、系统 Edge（`msedge.exe`，可用环境变量 `DSH_PARITY_EDGE` 覆盖路径）。

```powershell
# 1) 捕获 DSH 参考物（首次自动 npm install，锁定版本见 package.json）
powershell -NoProfile -ExecutionPolicy Bypass -File capture/capture-all.ps1

# 2) 生成 tokens.css（dsh-tokens.json → modules/apps/gui/webui/styles/tokens.css）
node generate/tokens2css.mjs

# 3) 本项目 WebUI 截图（与 DSH 捕获同管线，保证可比性）
node compare/screenshot-web.mjs ../../modules/apps/gui/webui/index.html dumps/webui-1440x900.png

# 4) 帧 diff（报告 + 热力图 + 网格归因 → dumps/）
node compare/diff-frames.mjs reference/dsh-web-1440x900.png dumps/webui-1440x900.png dumps

# 辅助：像素采样 / 区域放大（人工核查 golden）
node lib/sample.mjs reference/dsh-web-1440x900.png "280,500" "469,500"
node lib/zoom.mjs reference/dsh-web-1440x900.png dumps 8 "keys:165,70,105,42"
```

## 安全/隔离约定

- `DSH_HOME` 指向工具目录下 `.dsh-home/`（gitignored），不污染用户 `~/.dsh`
- `dsh web --no-open`，不会打开用户浏览器
- 结束按 PID 击杀 node 进程 + 兜底按命令行归属清扫，无残留
- token 不出日志（URL 中 token 以 REDACTED 写 meta）

## 踩坑记录

| 坑 | 根因 | 解法 |
|----|------|------|
| `msedge --headless --screenshot --virtual-time-budget` 永久挂起 | DSH UI 保持 WebSocket 长连接，虚拟时间永不推进 | 弃 CLI 截图，playwright-core CDP 主动 `page.screenshot()` |
| ESM 找不到 playwright-core | 脚本位置不在 node_modules 解析路径 | 脚本放工具目录内运行（node 从脚本位置向上解析） |
| 启动器被回收遗留 node 进程 | 沙箱子进程管理差异 | 单次调用内完成启动→捕获→击杀；CommandLine 归属兜底 |
| 首次运行参考物带"预览版说明"弹窗 / 主题漂移 | fresh DSH_HOME 首启弹窗；主题跟随系统 colorScheme | 捕获脚本自动关闭引导弹窗 + `colorScheme` 按参数固定 |
| DOM 存在的元素≠渲染可见 | 快捷键提示（kbd）DOM 有但视觉隐藏（仅悬停显示） | 复刻以 **golden 帧像素**为准；DOM 盒树只作布局规格参考 |
| file:// 页面 canvas 读图报 SecurityError | Chromium 跨文件污染 canvas | 对比用 Edge 加 `--allow-file-access-from-files`（仅本地核查场景） |
