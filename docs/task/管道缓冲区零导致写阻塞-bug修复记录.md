# 管道缓冲区大小为0导致写阻塞 — Bug修复记录

## 基本信息

| 项目 | 内容 |
|------|------|
| 发现日期 | 2026-09-16 |
| 修复日期 | 2026-09-16 |
| 严重级别 | P0 — E2E通信完全阻塞 |
| 影响范围 | MeshTransport / BusTransport / NamedPipeTransport 全部E2E测试 |
| 修复提交 | `7e0c40f4e` |

## 根因

`PipeAcceptLoop.RunAsync` 创建 `NamedPipeServerStream` 时使用5参数构造函数，未指定 `inBufferSize` / `outBufferSize`，默认值为 **0**。

```csharp
// ❌ 错误 — 默认缓冲区大小为0
var server = new NamedPipeServerStream(
    pipeName,
    PipeDirection.InOut,
    NamedPipeServerStream.MaxAllowedServerInstances,
    PipeTransmissionMode.Byte,
    PipeOptions.Asynchronous);
```

**缓冲区大小为0时，Windows命名管道的语义是：所有写操作阻塞，直到对端发起读操作。** 这不是"缓冲区很小"，而是"无缓冲"——数据直传，写端必须等读端就绪。

## 触发场景

MeshTransport 的 `HandlePeerConnectionAsync` 在读取握手后发送 ACK：

```csharp
var firstMsg = await BinaryProtocol.ReadAsync(server, ct);  // 读取握手 ✓
await server.WriteAsync(ack, ct);                            // 写ACK — 永久阻塞！
```

peer1（客户端）发送握手后**从不读取管道**（`MeshPeerConnection` 只有 `WriteLoopAsync` 写循环，无读循环）。因此 peer2（服务端）的 ACK 写入无人读取，缓冲区大小为0 → 写操作永久阻塞 → `HandlePeerConnectionAsync` 卡死在 `WriteAsync` → 后续 `ReadStreamAsync` 永远不执行 → peer1 发送的数据消息永远不被消费 → 测试5秒超时失败。

## 诊断过程

1. 添加 `[MESH-DIAG]` Console.Error.WriteLine 临时诊断到关键路径
2. 运行测试观察输出序列：
   ```
   peer2 收到握手: type=Control, payload=PEER:300001     ✓
   peer2 准备发送ACK, ct.IsCancellationRequested=False    ✓
   WriteLoop 消费消息: peer=300002, dataLen=37            ✓ (peer1写入队列)
   received=null                                          ✗ (5s超时)
   HandlePeerConnection 取消                              (DisposeAsync触发)
   ```
3. 关键发现：**"peer2 发送ACK完成" 从未打印** → `server.WriteAsync(ack, ct)` 阻塞
4. 排查 CancellationToken 状态：`ct.IsCancellationRequested=False`，排除取消
5. 排查管道连接状态：握手成功读取，管道已连接
6. 唯一剩余可能：**管道缓冲区为0导致写阻塞**（对端不读则写挂起）
7. 查阅 .NET 源码确认：5参数构造函数传 `inBufferSize=0, outBufferSize=0`

## 修复

### 修复1：显式指定管道缓冲区大小

```csharp
// ✅ 正确 — 显式指定65536字节缓冲区
const int PipeBufferSize = 65536;
var server = new NamedPipeServerStream(
    pipeName,
    PipeDirection.InOut,
    NamedPipeServerStream.MaxAllowedServerInstances,
    PipeTransmissionMode.Byte,
    PipeOptions.Asynchronous,
    inBufferSize: PipeBufferSize,
    outBufferSize: PipeBufferSize);
```

### 修复2：移除 MeshTransport 无用的ACK

MeshTransport 的 ACK 是服务端发送给客户端的握手确认，但客户端**从不读取**。ACK 不仅无用，还在缓冲区为0时导致死锁。直接移除：

```csharp
// ❌ 删除 — 客户端从不读取，缓冲区为0时死锁
var ack = BinaryProtocol.Encode(MessageType.Control, ProcessId, peerPid, ...);
await server.WriteAsync(ack, ct);

// ✅ 保留 — 直接进入消息读取循环
await foreach (var msg in BinaryProtocol.ReadStreamAsync(server, ct))
```

**注意**：BusTransport 和 NamedPipeTransport 的 ACK 保留，因为它们的从机有 `SlaveReceiveLoopAsync` 读循环会消费 ACK。

## 验证

- 165个单元测试全部通过（含6个E2E传输测试）
- 全量编译0警告0错误
- MeshTransport 双向通信测试通过

## 全局化改造方向（未来）

此bug暴露了一个系统性问题：**所有 `NamedPipeServerStream` 创建点都可能使用默认0缓冲区**。未来需要全局排查：

1. 搜索全项目所有 `new NamedPipeServerStream` 调用点
2. 逐个检查是否指定了缓冲区大小
3. 未指定的全部补上显式缓冲区大小（建议65536）
4. 提取常量 `PipeBufferSize` 到公共位置，统一引用

搜索命令：
```bash
rg "new NamedPipeServerStream" --type cs
```

**✅ 全局排查已完成（2026-09-16）**：全项目仅2处 `new NamedPipeServerStream`：
1. `lib/async_lock/ITransportTopology.cs` — `PipeAcceptLoop.RunAsync`（已修复）
2. `test/unit/testing.common/mock_server/PipeOpenAIMockServer.cs` — MockServer（已修复）

两处均已补上 `inBufferSize: 65536, outBufferSize: 65536`。

## 教训

1. **NamedPipeServerStream 5参数构造函数的默认缓冲区大小是0** — 这反直觉，大部分开发者假设有默认缓冲。必须显式指定。
2. **双向管道中单端只写不读会导致写阻塞** — 缓冲区为0时，写操作需要读端就绪。设计协议时必须确保双向都有读循环，或使用足够大的缓冲区。
3. **临时诊断日志有效但应系统化** — 本次用 `Console.Error.WriteLine` 手写诊断定位问题，但更应有一键开关机制（见 `TransportDiagnostics` 实现）。
