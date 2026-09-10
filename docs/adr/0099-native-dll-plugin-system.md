# 0099. Native DLL 插件系统: NativeLibrary.Load + UnmanagedCallersOnly

- 状态：proposed
- 日期：2026-09-11
- 决策者：AI + 用户确认

## 背景

项目已有三种插件加载方式，但 NativeAOT 下缺少真正的独立 DLL 插件能力：

| 方式 | AOT兼容 | 独立部署 | 限制 |
|------|:-------:|:-------:|------|
| 进程内插件 `LoadWorkflowPluginAsync<T>` | ✅ | ❌ | 同程序集编译，无法独立部署 |
| 外部exe进程 `ExternalPluginHost` | ✅ | ✅ | 进程开销大，IPC 序列化成本高 |
| 托管DLL (ALC) `DynamicPluginRegistry` | ❌ | ✅ | `LoadFromAssemblyPath` 不兼容 trimming，AOT 下返回空表 |

用户要求："所有形式都要做一次，然后用各种方式进行隔离使用即可。"

**核心矛盾**：NativeAOT 编译后没有 JIT，无法加载托管 IL DLL。但 `NativeLibrary.Load` 可以加载 **native DLL**（C++ 或 NativeAOT 编译的 C#），通过 C ABI 调用导出函数。

## 决策

实现 **native DLL 插件系统**：插件用 NativeAOT 编译为 native .dll，宿主用 `NativeLibrary.Load` 加载，通过 `[UnmanagedCallersOnly]` 导出的 C ABI 函数通信。

### 架构

```
Host (NativeAOT exe)
├── NativePluginLoader
│   ├── NativeLibrary.Load(pluginPath) → IntPtr handle
│   ├── NativeLibrary.GetExport(handle, "plugin_load") → IntPtr fn
│   ├── NativeLibrary.GetExport(handle, "plugin_invoke") → IntPtr fn
│   ├── NativeLibrary.GetExport(handle, "plugin_unload") → IntPtr fn
│   └── NativeLibrary.Free(handle) → 卸载
├── NativePluginHost (生命周期管理)
│   ├── Load → 加载DLL + 调用 plugin_load
│   ├── Invoke → JSON请求 → pinned buffer → plugin_invoke → 读响应
│   └── Unload → plugin_unload + NativeLibrary.Free
└── 通信协议: JSON over pinned byte buffer (无共享托管类型)

Plugin (NativeAOT dll)
├── [UnmanagedCallersOnly(EntryPoint = "plugin_load")]
│   └── int plugin_load(IntPtr configPtr, int configLen) → int (0=ok)
├── [UnmanagedCallersOnly(EntryPoint = "plugin_invoke")]
│   └── int plugin_invoke(IntPtr reqPtr, int reqLen, IntPtr respPtr, int respCap) → int (实际写入长度, 负数=错误)
└── [UnmanagedCallersOnly(EntryPoint = "plugin_unload")]
    └── int plugin_unload() → int (0=ok)
```

### Native ABI 契约

```c
// 所有函数使用 cdecl 调用约定（.NET 默认）

// 加载插件 — 传入 JSON 配置，返回 0=成功，非 0=错误码
int32_t plugin_load(const uint8_t* config_ptr, int32_t config_len);

// 调用方法 — 传入 JSON 请求，写 JSON 响应到 resp_ptr
// 返回值 > 0: 响应字节数; == 0: 空响应; < 0: 错误码
int32_t plugin_invoke(const uint8_t* req_ptr, int32_t req_len,
                      uint8_t* resp_ptr, int32_t resp_cap);

// 卸载插件 — 清理资源，返回 0=成功
int32_t plugin_unload(void);

// 可选: 查询插件元数据 — 返回 JSON 字符串
int32_t plugin_info(uint8_t* resp_ptr, int32_t resp_cap);
```

### JSON IPC 协议

请求:
```json
{"method": "echo", "args": {"text": "hello"}}
```

响应:
```json
{"ok": true, "value": "hello"}
```

错误:
```json
{"ok": false, "error": "method not found"}
```

### 内存管理

- **请求**: 宿主分配 `byte[]` + `fixed` pin，传指针给插件
- **响应**: 宿主预分配 buffer（cap=64KB），插件写入后返回实际长度
- **大响应**: 若 cap 不够，插件返回 `-2`（BufferTooSmall），宿主重试更大 buffer
- **所有权**: 宿主拥有所有 buffer，插件不分配/释放内存

### 卸载安全

- `plugin_unload` 先调用，让插件清理内部状态
- 然后 `NativeLibrary.Free(handle)` 卸载 DLL
- 卸载后禁止任何对插件函数的调用
- Windows: `FreeLibrary` 引用计数，多次 Load 需对应多次 Free
- Linux: `dlclose`，但卸载正在执行的代码是 UB，需确保无后台线程

## 替代方案

1. **只保留三种方式，不做 native DLL**（否决）：用户明确要求"所有形式都要做一次"。且 native DLL 是 AOT 下唯一能 in-process 加载独立插件的方式。

2. **用 COM/WinRT 互操作**（否决）：平台绑定 Windows，Linux 不可用。COM 注册复杂，NativeAOT 下 COM 支持有限。

3. **用 gRPC/Named Pipe in-process**（否决）：不是真正的 in-process，仍有 IPC 开销。且与外部 exe 进程方式重复。

4. **用 shared memory + 函数指针**（否决）：shared memory 适合大数据传输，但函数调用仍需 `NativeLibrary.GetExport`。可作为大响应的优化层，非基础架构。

## 后果

- **正面**：
  - AOT 兼容的 in-process 独立插件 — 填补四种方式的最后一块
  - 插件独立编译部署，不影响宿主编译
  - C ABI 标准化，未来可用 C++/Rust/Go 等语言写插件
  - `NativeLibrary.Free` 真卸载（比 ALC 更可靠）

- **负面**：
  - 无共享托管类型 — 所有数据走 JSON 序列化，有性能开销
  - 调试困难 — native DLL 内部异常不会传播到宿主
  - 平台绑定 — native DLL 不跨平台（win-x64/linux-x64 各编译一份）
  - `NativeLibrary.Free` 卸载正在执行的代码是 UB — 需严格的生命周期管理

- **中性**：
  - 插件 .csproj 需单独 `PublishAot=true` 编译流程
  - 需要文档记录 ABI 契约，第三方插件开发者需遵守
