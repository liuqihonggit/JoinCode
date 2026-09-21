namespace JoinCode.Abstractions.CodeIndex;

public interface IProgressiveDisclosure {
    /// <summary>异步按披露级别查询信息。</summary>
    Task<DisclosureResult> DiscloseAsync(string query, DisclosureLevel level, CancellationToken ct);
    /// <summary>异步扩展上一次披露结果。</summary>
    Task<DisclosureResult> ExpandAsync(DisclosureResult previous, CancellationToken ct);
}