# GUI 改造

源仓库：liuqihonggit/JoinCode；Fork：zhengjian211/JoinCode；分支：codex/gui-experience。

## 目录与范围

```text
app/gui/
  theming/           语义配色、强调色配置、共享动效
  persistence/      GUI 外观偏好
  view_models/      外观设置、会话筛选
  views/            主窗口、侧栏、设置抽屉
test/unit/join_code_gui.tests/  行为与 Skia 渲染验证
```

## 参考标准

- https://fluent2.microsoft.design/color — 语义 token、色彩层次、对比度。
- https://fluent2.microsoft.design/motion — 短动效反馈操作、避免影响阅读。
- https://fluent2.microsoft.design/accessibility — 键盘可达，状态不只依靠颜色。
- https://docs.avaloniaui.net/docs/graphics-animation/control-transitions — 原生过渡。

## 验收

- [x] 文件驱动的八种强调色，支持四个基础主题；原生 Fluent 控件同步配色。
- [x] GUI 主题与动效偏好重启恢复。
- [x] 按钮、卡片、抽屉、忙碌状态动效；关闭动效后立即反馈。
- [x] 会话标题实时筛选、空结果提示、Ctrl+K 聚焦。
- [x] 主窗口布局与设置分组改造；800px 窗口仍可访问发送操作。
- [x] Markdown 文件导出，保留代码块，排除系统提示词注入。
- [x] Debug 编译、514 项 GUI 单元与渲染测试全部通过、五张 Skia 截图审查。
- [ ] 提交并推送到 Fork 功能分支。

## 备份

上游完整 Git bundle 保存在 ../JoinCode-upstream-backup.bundle。原开发工程未修改。

## 验证与运行

```powershell
dotnet test test/unit/join_code_gui.tests/JoinCodeGui.Tests.csproj -c Debug -m:4
dotnet publish app/gui/JoinCodeGui.csproj -c Debug -r win-x64 --self-contained true -p:PublishAot=false -p:PublishSingleFile=false -o artifacts/publish/JoinCode.Gui -m:4
.\artifacts\publish\JoinCode.Gui\JoinCode.Gui.exe
```

编译源码要求 .NET 10.0.301+ SDK。发布目录包含运行时，可直接运行 EXE，无需另外安装 .NET 10。首次使用真实模型需按上游说明配置供应商和 API Key。此次验证未向付费模型发送请求。

截图：`dumps/gui_experience/`；测试报告：`test/unit/join_code_gui.tests/TestResults/gui-tests.trx`（诊断产物不提交）。

<!-- 🤖 Auto Decision: 使用现有 Avalonia 门面、文件驱动强调色和原生过渡，避免另建 Web GUI。验证：Debug 编译与 514 项 GUI 测试通过。 -->
<!-- 🤖 Auto Decision: 保留原会话选中背景，用强调色描边增强选中反馈；渲染测试按真实卡片几何边界采样。 -->
<!-- 🤖 Auto Decision: 快捷操作采用受约束的 WrapPanel，固定保留发送区域；窄窗口失败测试修复后通过，截图复核无发送裁切。 -->
