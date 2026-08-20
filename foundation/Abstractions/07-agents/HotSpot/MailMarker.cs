namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 延迟邮件标记 — 分类便于队员判断优先级
/// </summary>
public enum MailMarker
{
    /// <summary>
    /// 热文件冲突 — 高优先级，涉及契约变更
    /// </summary>
    [EnumValue("hot_file_conflict")] HotFileConflict,

    /// <summary>
    /// 测试文件冲突 — 中优先级，测试用例变更
    /// </summary>
    [EnumValue("test_file_conflict")] TestFileConflict,

    /// <summary>
    /// 资源引用变更 — 低优先级，如配置/依赖变更
    /// </summary>
    [EnumValue("resource_ref_change")] ResourceRefChange
}
