namespace JoinCode.Abstractions.CodeIndex;

public interface IProgressiveDisclosure
{
    Task<DisclosureResult> DiscloseAsync(string query, DisclosureLevel level, CancellationToken ct);
    Task<DisclosureResult> ExpandAsync(DisclosureResult previous, CancellationToken ct);
}
