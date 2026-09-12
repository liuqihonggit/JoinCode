
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Notebook服务接口
/// </summary>
public interface INotebookService
{
    /// <summary>
    /// 加载Notebook文件
    /// </summary>
    Task<NotebookDocument?> LoadAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存Notebook文件
    /// </summary>
    Task<bool> SaveAsync(string filePath, NotebookDocument notebook, CancellationToken cancellationToken = default);

    /// <summary>
    /// 创建新的Notebook
    /// </summary>
    NotebookDocument Create(string? kernelName = null, string? language = null);

    /// <summary>
    /// 添加单元格
    /// </summary>
    NotebookEditResult AddCell(NotebookDocument notebook, NotebookCellType cellType, string content, int? index = null);

    /// <summary>
    /// 删除单元格
    /// </summary>
    NotebookEditResult DeleteCell(NotebookDocument notebook, int index);

    /// <summary>
    /// 编辑单元格内容
    /// </summary>
    NotebookEditResult EditCell(NotebookDocument notebook, int index, string newContent, string? newCellType = null);

    /// <summary>
    /// 移动单元格
    /// </summary>
    NotebookEditResult MoveCell(NotebookDocument notebook, int fromIndex, int toIndex);

    /// <summary>
    /// 更改单元格类型
    /// </summary>
    NotebookEditResult ChangeCellType(NotebookDocument notebook, int index, NotebookCellType newType);

    /// <summary>
    /// 执行单元格（模拟）
    /// </summary>
    NotebookEditResult ExecuteCell(NotebookDocument notebook, int index, string? output = null);

    /// <summary>
    /// 清除所有输出
    /// </summary>
    NotebookEditResult ClearAllOutputs(NotebookDocument notebook);

    /// <summary>
    /// 获取单元格内容
    /// </summary>
    string? GetCellContent(NotebookDocument notebook, int index);

    /// <summary>
    /// 列出所有单元格
    /// </summary>
    List<(int Index, NotebookCellType Type, string Preview)> ListCells(NotebookDocument notebook);
}
