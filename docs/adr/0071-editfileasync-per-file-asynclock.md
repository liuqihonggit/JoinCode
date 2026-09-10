# 0071. IFileSystem.EditFileAsync 原子编辑接口 — per-file AsyncLock 串行化

- 状态：accepted
- 日期：2026-09-07
- 决策者：项目架构组

## 背景

FileEditLogic、ApplyPatchLogic、ThrottledFileService、ConfigLoader、HookConfigurationManager、LoginCommand、McpServerConfigStore 等上层服务采用 Read→改→Write 模式编辑文件：
1. `ReadAllTextAsync` 读文件内容
2. 内存中修改
3. `WriteAllTextAsync` 写回

问题：两个并发编辑同一文件时会产生**丢失更新**（A 读 → B 读 → A 写 → B 写，B 的写覆盖 A 的修改）。配置文件（settings.json、auth.json、mcp_servers.json、hooks.json）和状态文件（PID 文件）的并发竞态风险最高。

## 决策

### 决策1：IFileSystem 加 EditFileAsync\<T> 泛型接口

**接口签名**：
```csharp
Task<T> EditFileAsync<T>(
    string path,
    Func<byte[], CancellationToken, Task<(byte[]? NewContent, T Result)>> transform,
    CancellationToken cancellationToken = default);
```

**语义**：
- 读取文件字节 → 调用 transform → 写回新字节
- transform 返回 `null` 表示不写入（取消编辑），只返回 Result
- 文件不存在时抛 `FileNotFoundException`
- 返回 transform 的附加结果 T

**理由**：
- 泛型 T 允许调用方返回编辑结果（如 FileEditResult、FileLineEditResult）
- byte[] 而非 string，让 transform 处理编解码（支持 UTF-8/UTF-16 等多编码）
- transform 内部用 `FileEncodingDetector.DecodeBytes`/`EncodeString` 处理 BOM

### 决策2：PhysicalFileSystem 用 per-file AsyncLock 串行化

**实现**：
```csharp
private readonly ConcurrentDictionary<string, AsyncLock> _editLocks = new();

public async Task<T> EditFileAsync<T>(...)
{
    var editLock = _editLocks.GetOrAdd(normalizedPath, p => new AsyncLock($"EditFile:{p}"));
    var releaser = await editLock.TryLockAsync(ct);
    using (releaser)
    {
        var bytes = await ReadAllBytesAsync(path, ct);
        var (newContent, result) = await transform(bytes, ct);
        if (newContent is not null)
            await WriteAllBytesAsync(path, newContent, ct);
        return result;
    }
}
```

**理由**：
- 用项目封装的 `AsyncLock`（非裸 `SemaphoreSlim`），复用 `LockRegistry` 死锁诊断
- per-file 锁：同一文件串行，不同文件并行
- 锁按规范路径缓存，生命周期与 PhysicalFileSystem（Singleton）相同
- `OnDispose` 逐个释放 AsyncLock 避免内核句柄泄漏

### 决策3：上层服务只调接口，不加锁

所有 Read→改→Write 模式的上层服务改用 `_fs.EditFileAsync`，transform 内部做编解码 + 修改逻辑。上层无锁，底层管道保证串行。

**改造范围**（按文件类型分组）：

| 分组 | 文件 | 方法 | 说明 |
|------|------|------|------|
| 文件编辑工具 | FileEditLogic.cs | 4 方法 | 文件编辑核心工具 |
| 文件编辑工具 | ApplyPatchLogic.cs | ApplyAsync | patch 应用 |
| 文件编辑工具 | ThrottledFileService.cs | EditFileAsync + EditByLineRangeAsync | 限速文件服务 |
| 文件编辑工具 | ShellSedInterceptMiddleware.cs | 确认阶段 | sed 拦截中间件 |
| 配置文件 | ConfigLoader.cs | SaveApiKeyToJccAsync + SaveSettingToSettingsJsonAsync + SaveSettingToGlobalConfigAsync | auth.json + settings.json + global.json |
| 配置文件 | HookConfigurationManager.cs | AddHookAsync + RemoveHookAsync | hooks.json |
| 配置文件 | LoginCommand.cs | SaveAuthAsync | auth.json |
| 配置文件 | LogoutCommand.cs | ExecuteAsync | auth.json |
| 配置文件 | McpServerConfigStore.cs | AddServerAsync + RemoveServerAsync | mcp_servers.json |
| 配置文件 | JccChatSession.cs | UpdateVendorProfileModelAsync | settings.json (GUI) |
| 状态文件 | ConcurrentSession.cs | UpdateAsync | PID 文件 |

**评估后跳过的文件**（无需改造）：

| 文件 | 跳过原因 |
|------|----------|
| SettingsLoader.cs | 只有纯读或纯写，无 Read→改→Write 竞态 |
| GraphPersistence.cs | 已分离 Save+Load，Save 用 PersistencePipeline |
| HighWaterMarkManager.cs | 已用 FileLockService 跨进程锁 |
| TranscriptFileWriter.cs | 已用 AsyncLock 保护追加 |
| TrustFolderManager.cs | 接口为同步（void），EditFileAsync 为异步，改签名影响面大；CLI 启动时单次执行，并发风险低 |
| RenameCommand.cs | 写回后还需 MoveFile，EditFileAsync 不保护后续 MoveFile；/rename 低频手动操作 |
| TeamMemorySyncService.cs | 纯写入（远程内容覆盖本地），无 Read→改→Write 竞态 |
| SourceCodePatcher.cs | Doctor Agent 单进程操作，并发风险低 |
| BootstrapLoop.cs | Doctor Agent 单进程操作，并发风险低 |

### 决策4：提取 DecodeBytes/EncodeString 到 FileEncodingDetector 共享

`FileEncodingDetector.DecodeBytes(byte[]) → (string, Encoding)` 和 `EncodeString(string, Encoding) → byte[]` 作为静态方法，FileEditLogic 和 ApplyPatchLogic 共享，消除重复。

## 替代方案

1. **全局单锁**：所有文件编辑共享一把 AsyncLock。放弃：不同文件无竞态，全局锁降低并行度
2. **FileLockService 跨进程锁**：用 OS 级文件锁。放弃：同进程内无跨进程需求，OS 锁开销大且平台差异
3. **ReaderWriterLockSlim**：读写分离。放弃：编辑是 Read→改→Write 原子操作，读写分离无意义
4. **无锁 CAS 乐观并发**：重试机制。放弃：文件编辑非内存操作，CAS 不适用

## 验证

- 6 个单元测试通过（`EditFileAsyncTests`）：
  - 同一文件 50 并发无丢失更新 ✅
  - 不同文件并行 ✅
  - 文件不存在抛 FileNotFoundException ✅
  - transform 返回 null 不写入 ✅
  - transform 接收当前内容 ✅
  - 顺序编辑组合正确 ✅
- FileEditLogic 16 测试通过
- ApplyPatchLogic 14 测试通过
- ThrottledFileService 8 测试通过
- ApiKeySaveLoadTests 7 测试通过（ConfigLoader 改造后）

## 影响

| 文件 | 改动 |
|------|------|
| `IFileSystem.cs` | 加 `EditFileAsync<T>` 接口 |
| `PhysicalFileSystem.cs` | 实现 + per-file AsyncLock + OnDispose |
| `InMemoryFileSystem.cs` (Infrastructure + Testing.Common) | 实现 + per-file AsyncLock |
| `FileEditLogic.cs` | 4 方法改用 EditFileAsync + 移除旧 helper |
| `ApplyPatchLogic.cs` | ApplyAsync 改用 EditFileAsync |
| `ThrottledFileService.cs` | EditFileAsync + EditByLineRangeAsync 改用 |
| `ShellSedInterceptMiddleware.cs` | 确认阶段改用 EditFileAsync |
| `ConfigLoader.cs` | 3 个 Save 方法改用 EditFileAsync |
| `HookConfigurationManager.cs` | AddHook/RemoveHook 改用 EditFileAsync + 提取 EditHooksFileAsync helper |
| `LoginCommand.cs` | SaveAuthAsync 改用 EditFileAsync |
| `LogoutCommand.cs` | ExecuteAsync 的 auth.json 读写改用 EditFileAsync |
| `McpServerConfigStore.cs` | AddServer/RemoveServer 改用 EditFileAsync |
| `JccChatSession.cs` | UpdateVendorProfileModelAsync 改用 EditFileAsync |
| `ConcurrentSession.cs` | UpdateAsync 改用 EditFileAsync |
| `FileEncodingDetector.cs` | 加 DecodeBytes + EncodeString 共享方法 |
| `EditFileAsyncTests.cs` | 6 个并发安全与原子性测试 |
| `GlobalUsings.cs` (Guard + Composition + Bridge + JoinCodeGui) | 加 `Infrastructure.IO.Services.FileOps` |
