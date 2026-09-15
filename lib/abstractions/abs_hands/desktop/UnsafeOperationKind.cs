namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 不可逆操作分类 — 撤销元意识（PRD U-01）的安全护栏埋点
/// </summary>
public enum UnsafeOperationKind
{
    /// <summary>安全操作</summary>
    [EnumValue("none")] None,

    /// <summary>文件删除</summary>
    [EnumValue("file_delete")] FileDelete,

    /// <summary>关闭窗口（可能丢失未保存数据）</summary>
    [EnumValue("window_close")] WindowClose,

    /// <summary>结束进程</summary>
    [EnumValue("process_terminate")] ProcessTerminate,

    /// <summary>危险坐标点击（如"确定删除"按钮）</summary>
    [EnumValue("dangerous_coordinate")] DangerousCoordinate,
}
