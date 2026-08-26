## Unshipped Releases

### Next

New Diagnostics:

- JCC1007: AOT incompatible: System.Reflection.Emit is not supported under NativeAOT.
- JCC1013: AOT risk: Assembly.Load may fail under NativeAOT due to trimming.
- JCC1014: AOT risk: Type.GetType(string) may return null under NativeAOT due to trimming.
- JCC1015: AOT risk: Activator.CreateInstance(Type) may fail under NativeAOT without trimmer root.
- JCC1016: AOT risk: MethodInfo.Invoke has poor performance under NativeAOT.
- JCC11001: 容器初始化: 容器类型字段/属性必须初始化，禁止为 null。可空容器（List<T>?）允许，构造函数赋值自动豁免。
