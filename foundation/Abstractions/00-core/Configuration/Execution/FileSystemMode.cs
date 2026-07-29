namespace JoinCode.Abstractions.Configuration;

/// <summary>
/// 文件系统后端模式枚举 — 通过 JCC_FILE_SYSTEM_MODE 环境变量一键切换
/// Physical=真实磁盘（默认）, InMemory=纯内存0磁盘IO（调试/E2E测试用）
/// </summary>
public enum FileSystemMode : byte
{
    [EnumValue("Physical")][DisplayText("物理磁盘")] Physical,
    [EnumValue("InMemory")][DisplayText("内存文件系统")] InMemory
}
