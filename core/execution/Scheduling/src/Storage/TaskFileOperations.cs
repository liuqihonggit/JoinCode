using JoinCode.Abstractions.Attributes;

namespace Core.Scheduling;

/// <summary>
/// 聚合任务文件操作所需的全部依赖，简化 FileBasedTaskService 构造函数参数
/// </summary>
[Register]
public sealed record TaskFileOperations(
    IFileOperationService FileOperationService,
    ITaskFileWriter TaskFileWriter,
    ITaskFileReader TaskFileReader,
    IFileSystem FileSystem)
{
    /// <summary>
    /// 从服务提供者创建 TaskFileOperations 实例，用于 DI 注册
    /// </summary>
    public static TaskFileOperations FromServiceProvider(IServiceProvider sp)
    {
        return new TaskFileOperations(
            sp.GetRequiredService<IFileOperationService>(),
            sp.GetRequiredService<ITaskFileWriter>(),
            sp.GetRequiredService<ITaskFileReader>(),
            sp.GetRequiredService<IFileSystem>());
    }
}
