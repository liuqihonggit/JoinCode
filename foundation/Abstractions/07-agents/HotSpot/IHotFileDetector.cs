namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 热文件检测器 — 启发式规则判断文件是否为"热文件"（契约变更会波及调用方的文件）
/// 通用支持 C#/Java/Python/JS/Go 等，不依赖目标项目的源码标记
/// 热文件 = 接口/公共契约/配置/模块入口，改了会触发热点识别收口
/// </summary>
public interface IHotFileDetector
{
    /// <summary>
    /// 判断单个文件是否为热文件
    /// </summary>
    /// <param name="filePath">文件相对或绝对路径</param>
    /// <returns>true=热文件（契约变更波及调用方），false=普通文件</returns>
    bool IsHotFile(string filePath);

    /// <summary>
    /// 批量检测热文件
    /// </summary>
    /// <param name="filePaths">文件路径集合</param>
    /// <returns>热文件集合（保持输入顺序去重）</returns>
    IReadOnlySet<string> DetectHotFiles(IEnumerable<string> filePaths);
}
