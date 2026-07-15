using JoinCode.Abstractions.Attributes;

namespace Core.Memdir;

/// <summary>
/// Memdir 组件配置选项
/// </summary>
[Register]
public sealed partial class MemdirOptions
{
    /// <summary>
    /// 存储根路径
    /// </summary>
    public string StoragePath { get; set; } = Path.Combine(AppContext.BaseDirectory, "memdir");

    public MemdirOptions() { }

    /// <summary>
    /// DI 构造函数 — 从 WorkflowConfig 获取 StoragePath
    /// </summary>
    public MemdirOptions(WorkflowConfig? config)
    {
        StoragePath = config?.MemdirPath ?? Path.Combine(AppContext.BaseDirectory, "memdir");
    }
}
