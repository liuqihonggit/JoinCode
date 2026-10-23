namespace JoinCode.Cli.Bridge;

/// <summary>
/// 图片附件 DTO（TASK066 板块 G G4 — submit 请求 images 字段元素）— native_tui 侧
/// <c>NativeTui.Core.Bridge.BridgeImageAttachment</c> 的 jcc 侧镜像（ADR 0141 进程协议常态，
/// 两端各自定义 DTO，JSON 形状由 BridgeJsonContractTests 锁定防漂移）：
/// 三字段最小形态（base64/mediaType/filename camelCase），与引擎侧回合链解耦。
/// </summary>
/// <param name="Base64">图片数据 base64（TUI 侧 Core ImageAttachment.Base64 原文透传）。</param>
/// <param name="MediaType">MIME 类型（如 image/png — 原文透传）。</param>
/// <param name="Filename">显示名（原文透传 — 引擎侧降级投影 [Image #N: filename (mediaType)] 消费）。</param>
public sealed record BridgeImageAttachment(string Base64, string MediaType, string Filename);
