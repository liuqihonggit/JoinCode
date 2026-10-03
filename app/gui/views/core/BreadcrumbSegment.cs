namespace JoinCode.Gui.Views;

/// <summary>面包屑段 — 路径段名+完整路径，点击在资源管理器中打开对应目录</summary>
public sealed record BreadcrumbSegment(string Name, string FullPath);
