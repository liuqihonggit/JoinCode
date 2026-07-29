
namespace JoinCode.Abstractions.Models.Todo;

/// <summary>
/// 待办事项
/// </summary>
public sealed record TodoItem(
    [Required(ErrorMessage = "id 不能为空")]
    [StringLength(50, ErrorMessage = "ID过长")]
    string Id,
    [Required(ErrorMessage = "content 不能为空")]
    [StringLength(500, ErrorMessage = "内容过长")]
    string Content,
    [Required(ErrorMessage = "status 不能为空")]
    [StringLength(20, ErrorMessage = "状态过长")]
    string Status,
    [Required(ErrorMessage = "priority 不能为空")]
    [StringLength(20, ErrorMessage = "优先级过长")]
    string Priority,
    [StringLength(50, ErrorMessage = "父ID过长")]
    string? ParentId = null,
    [StringLength(500, ErrorMessage = "ActiveForm过长")]
    string? ActiveForm = null,
    DateTime? CreatedAt = null,
    DateTime? UpdatedAt = null);

/// <summary>
/// 待办输入项（用于写入）— 对齐 TS TodoItemSchema
/// TS 字段: content(必填), status(必填), activeForm(必填)
/// CS 扩展: id(可选,自动生成), priority(可选,默认medium), parentId(可选)
/// </summary>
public sealed record TodoItemInput(
    [StringLength(50, ErrorMessage = "ID过长")]
    string? Id = null,
    [Required(ErrorMessage = "content 不能为空")]
    [StringLength(500, ErrorMessage = "内容过长")]
    string Content = "",
    [Required(ErrorMessage = "status 不能为空")]
    [StringLength(20, ErrorMessage = "状态过长")]
    string Status = "",
    [StringLength(20, ErrorMessage = "优先级过长")]
    string? Priority = null,
    [StringLength(50, ErrorMessage = "父ID过长")]
    string? ParentId = null,
    [Required(ErrorMessage = "activeForm 不能为空")]
    [StringLength(500, ErrorMessage = "ActiveForm过长")]
    string ActiveForm = "");

/// <summary>
/// 待办写入命令
/// </summary>
public sealed record TodoWriteCommand(
    [Required(ErrorMessage = "todos 不能为空")]
    List<TodoItemInput> Todos);

/// <summary>
/// 待办查询命令
/// </summary>
public sealed record TodoListCommand(
    [StringLength(20, ErrorMessage = "状态过长")]
    string? Status = null,
    [StringLength(20, ErrorMessage = "优先级过长")]
    string? Priority = null,
    bool IncludeCompleted = false);
