# 0088. 测试执行规则

- 状态：accepted
- 日期：2026-09-08
- 决策者：项目架构组

## 背景

全局测试需要限时、快速、不卡死。本文档定义测试命令、卡死排查优先级与定位副作用测试的 throw 探针法。

## 详细内容

## 测试

```powershell
dotnet test App.slnx -c Release /p:SkipLocalPack=true --filter "Category!=Integration"
```

1. 每个测试都加入一个限时10s，再去找到高耗时。
2. 一旦无法全局测试，出现卡死，就停下来修复全局测试，确保永远都是快速的全局测试。
3. **⚠️ 测试"卡死"排查优先级**（按成本从低到高）：
   - **先查残留 testhost**：`Get-Process -Name testhost | Stop-Process -Force` — 之前 `dotnet test` 被强杀时 testhost 子进程存活，锁住编译产物 DLL 导致后续构建报 `MSB3027 超出重试计数`，表象也是"卡死"。
   - **再查 stdout 管道锁**：`RedirectStandardOutput + ReadToEnd()` 一次性读取会因输出量大缓冲填满而死锁。正确做法：用重定向 `>` 写日志文件再查，不用管道。
   - **最后才是测试逻辑死锁**：见下方"GUI 异步测试"经验。
4. **🔍 定位未标记 Integration 的副作用测试（throw 探针法）**：
   - **场景**：某测试操作鼠标/键盘/文件系统等副作用，但没标 `[Trait("Category", "Integration")]`，导致 `--filter "Category!=Integration"` 仍会触发副作用。
   - **方法**：把可疑的生产方法函数体改成 `throw new NotImplementedException("XXX disabled for testing")`（保留方法签名，编译不报错），然后 `dotnet test` **不带 filter** 运行全部测试，哪些测试抛 `NotImplementedException` 就是哪些测试在调用该方法。
   - **示例**：把 `Win32DesktopInputService.ClickAsync` 和 `TypeTextAsync` 方法体改成 throw，跑 `dotnet test tests/Unit/Hands.Tests/Hands.Tests.csproj`，5个测试抛异常 → 这5个就是操作鼠标的测试。
   - **恢复**：定位完成后 `git checkout -- <文件>` 恢复原始代码。
   - **优势**：比 grep 搜索更准确——能找到间接调用链（如测试调用 `CompoundOperationToolHandlers.MultiClickAsync`，内部再调用 `ClickAsync`），grep 只能找到直接调用。

## 替代方案

无。throw 探针法是定位间接调用链的最准确方法，grep 无法替代。
