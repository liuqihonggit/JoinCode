# 0071. IFileSystem.EditFileAsync 原子编辑接口 — per-file AsyncLock 串行化

- 状态：accepted
- 日期：2026-09-07
- 决策者：项目架构组

## 背景

FileEditLogic、ApplyPatchLogic、ThrottledFileService 等上层服务采用 Read→改→Write 模式编辑文件：
1. `ReadAllTextAsync` 读文件内容
2. 内存中修改
3. `WriteAllTextAsync` 写回

问题：两个并发编辑同一文件时会产生**丢失更新**（A 读 → B 读 → A 写 → B 写，B 的写覆盖 A 的修改）。

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

FileEditLogic、ApplyPatchLogic、ThrottledFileService 改用 `_fs.EditFileAsync`，transform 内部做编解码 + 修改逻辑。上层无锁，底层管道保证串行。

### 决策4：提取 DecodeBytes/EncodeString 到 FileEncodingDetector 共享

`FileEncodingDetector.DecodeBytes(byte[]) → (string, Encoding)` 和 `EncodeString(string, Encoding) → byte[]` 作为静态方法，FileEditLogic 和 ApplyPatchLogic 共享，消除重复。

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

## 影响

| 文件 | 改动 |
|------|------|
| `IFileSystem.cs` | 加 `EditFileAsync<T>` 接口 |
| `PhysicalFileSystem.cs` | 实现 + per-file AsyncLock + OnDispose |
| `InMemoryFileSystem.cs` (Infrastructure + Testing.Common) | 实现 + per-file AsyncLock |
| `FileEditLogic.cs` | 4 方法改用 EditFileAsync + 移除旧 helper |
| `ApplyPatchLogic.cs` | ApplyAsync 改用 EditFileAsync |
| `ThrottledFileService.cs` | EditFileAsync + EditByLineRangeAsync 改用 |
| `FileEncodingDetector.cs` | 加 DecodeBytes + EncodeString 共享方法 |
| `EditFileAsyncTests.cs` | 6 个并发安全与原子性测试 |
