## Unshipped Releases

### Next

New Diagnostics:

- JCC1007: AOT incompatible: System.Reflection.Emit is not supported under NativeAOT.
- JCC1013: AOT risk: Assembly.Load may fail under NativeAOT due to trimming.
- JCC1014: AOT risk: Type.GetType(string) may return null under NativeAOT due to trimming.
- JCC1015: AOT risk: Activator.CreateInstance(Type) may fail under NativeAOT without trimmer root.
- JCC1016: AOT risk: MethodInfo.Invoke has poor performance under NativeAOT.
- JCC11001: 容器初始化: 容器类型字段/属性必须初始化，禁止为 null。可空容器（List<T>?）允许，构造函数赋值自动豁免。
- JCC11002: 可空容器: 可空容器字段/属性建议改为非空初始化（= []），避免 null 检查。
- JCC9301: 内存泄漏: IDisposable字段未在Dispose/DisposeAsync中释放。
- JCC9302: 内存泄漏: 可空IDisposable字段释放后未置null。
- JCC9303: 非托管资源: IntPtr/UIntPtr字段应改用SafeHandle模式。
- JCC9304: 释放顺序: base.Dispose()必须在Dispose方法体最后位置。
