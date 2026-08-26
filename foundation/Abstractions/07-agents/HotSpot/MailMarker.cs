namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 延迟邮件标记 — [Flags] 位标志枚举，一封邮件可同时携带多个冲突标记
/// [EnumValue] 特性由 EnumMetadataGenerator 自动生成 MailMarkerConstants + MailMarkerExtensions
/// 用法: mail.Marker.HasFlag(MailMarker.HotFileConflict) 或 mail.Marker = MailMarker.HotFileConflict | MailMarker.TestFileConflict
/// </summary>
[Flags]
public enum MailMarker
{
    /// <summary>无标记</summary>
    [EnumValue("none")] None = 0,

    /// <summary>热文件冲突 — 高优先级，涉及契约变更</summary>
    [EnumValue("hot_file_conflict")] HotFileConflict = 1,

    /// <summary>测试文件冲突 — 中优先级，测试用例变更</summary>
    [EnumValue("test_file_conflict")] TestFileConflict = 2,

    /// <summary>资源引用变更 — 低优先级，如配置/依赖变更</summary>
    [EnumValue("resource_ref_change")] ResourceRefChange = 4
}
