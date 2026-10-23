namespace NativeTui.Core.Bridge;

/// <summary>
/// 图片附件 DTO（TASK066 板块 G G3 — E4 引擎协议图片提交通道）— bridge 协议层自有 DTO：
/// 三字段最小形态（Base64/MediaType/Filename），与 Core attachments 域 <c>ImageAttachment</c>
/// 解耦（对齐 DiffModels 先例 — bridge 域自有消息模型，Core 域渲染/编辑关注的 Id/Dimensions/
/// CachePath 等不进引擎协议）；Core → bridge 投影由 ReplShell 提交时刻完成
///（<c>TakeSubmittedAttachments</c> 冻结快照 → 引擎提交参数）。
/// </summary>
/// <param name="Base64">图片数据 base64（Core ImageAttachment.Base64 原文透传）。</param>
/// <param name="MediaType">MIME 类型（如 image/png — Core ImageAttachment.MediaType 原文透传）。</param>
/// <param name="Filename">显示名（Core ImageAttachment.Filename 原文透传）。</param>
public sealed record BridgeImageAttachment(string Base64, string MediaType, string Filename);
