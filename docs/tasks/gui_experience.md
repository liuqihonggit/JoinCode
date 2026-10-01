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

- [ ] 文件驱动的多强调色，支持四个基础主题。
- [ ] GUI 主题与动效偏好重启恢复。
- [ ] 按钮、卡片、抽屉、忙碌状态动效；关闭动效后立即反馈。
- [ ] 会话标题实时筛选、空结果提示。
- [ ] 主窗口布局与设置分组改造。
- [ ] Debug 编译、GUI 单元与渲染测试、截图审查。
- [ ] 提交并推送到 Fork 功能分支。

## 备份

上游完整 Git bundle 保存在 ../JoinCode-upstream-backup.bundle。原开发工程未修改。
