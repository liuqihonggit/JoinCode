# 上游 ONNX / RAG 同步

上游：liuqihonggit/JoinCode，main 8b74ae37。
同步前 GUI 分支：d7cd0090；已保留 codex/gui-before-onnx-20261001 与 .xxx/pre-onnx-merge.bundle。

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---|---|---|---|
| 上游 main 8b74ae37 | 按 ADR 0078 rebase，保留 GUI 改造 | 是，5 个提交成功重放，无文本冲突 | 是，upstream/main 为 HEAD 祖先 |
| 上游 ONNX / RAG 和 SwissTable 基建 | GUI、CodeIndex 与工具注册表 | 是，恢复依赖并编译成功 | 是，539 GUI + 757 CodeIndex + 20 注册表测试通过 |
| Windows 自包含运行时与 ONNX 原生库 | artifacts/publish/joincode_gui_onnx | 是，发布零错误 | 是，实际 EXE 窗口启动，正常关闭退出码 0，按 ADR 0080 验证 |
| 同步后的 GUI 功能分支 | Fork zhengjian211/JoinCode | 是，保留旧历史备份 | 推送时明确校验原远程哈希，防止覆盖他人更新 |

最新上游增量为 2 个提交：ONNX doctor 诊断与 SwissTable/HAMT 引用调整；ONNX embedding 实现已存在于共同基线中。

## 验证范围与发布

总计 1316 项测试通过、零失败；实际模型文件未下载，未把缺模型后直接返回的 ONNX 推理测试当成验收通过。
GUI 改造与上游 ONNX 实现均保留，不改写上游检索逻辑。
第一次覆盖原发布目录因现有 GUI 进程 11972 占用 DLL 而失败，改为独立目录发布后成功。
验证新版只关闭自行启动的测试进程 24612，原有用户窗口继续运行。
`artifacts/启动JoinCode.lnk` 已指向新版 `artifacts/publish/joincode_gui_onnx/JoinCode.Gui.exe`；无需安装系统 .NET。
构建产生的 GUI bin 将再次移入根目录 .xxx，继续避免用户误启动非自包含版本。
