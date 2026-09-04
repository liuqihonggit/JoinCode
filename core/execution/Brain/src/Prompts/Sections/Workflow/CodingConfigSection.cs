namespace Core.Prompts.Sections;

/// <summary>
/// 编码推荐配置部分 - 架构选型/效率/编译详细配置（按关键词触发注入，不进首次提示词）
/// </summary>
[PromptSection(
    Name = "coding_config",
    Order = 6,
    InjectOn = PromptSectionInject.Keyword,
    Keywords = new[] {
        "性能", "优化", "架构", "效率", "编译", "队列", "缓存", "重构",
        "热路径", "扫盘", "字典", "状态机", "中间件", "异步锁", "断点续传",
        "AOT", "GC", "SIMD", "NativeAOT", "LRU", "0-GC", "多线程"
    })]
public static class CodingConfigSection {
    public static string GetContent() {
        return """
# 编码推荐配置

默认行为是快速路径，能写三行就不引入架构；效率工具是知道热路径才进行优化，按照热点优化而不是全局。

## 架构选型
1. 可复用和归纳的函数、类、枚举，仅写一套：它们非常类似也要尽可能写成一个，避免用户难以理解
2. 泛型模块化
3. 状态机（状态查表事件，动作转移）
4. 中间件洋葱模型
5. 任何资源类都必须树状生长：这不是反模式设计，这是 is-a，不是鸵鸟问题（重写 fly）。否则难以收集到容器 map[typeName,object]，设计资源生命周期，提供插件卸载。后台会不断扫描这些资源健康，如果被卸载或者宿主死亡了会自动破坏

## 效率
1. 计算字符串需要 0-GC，效率拉满，学习 1BRC 操作：多线程 + SIMD + 非托管字符数组指针直写
2. 纯异步函数来处理 IO，高性能内存数据结构用同步函数。异步锁要自己封装 AsyncLock，利用 using 释放，锁内超时报错 key 名，避免同步锁定异步的情况
3. 下载用多线程和断点续传
4. 直接硬件支持的函数，例如 CRC 等等
5. 热路径优化
6. 无锁队列、环形队列、可排序结构、LRU 缓存结构、Everything 扫盘 + 索引

## 编译
NativeAOT 编译，内联函数特性支持。如果当前项目不是这样的，就不要改变用户已配置的，除非用户明确要求。
""";
    }
}
