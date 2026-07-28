namespace JoinCode.Abstractions.Brain.Context.Resolution;

public interface IReferenceResolver
{
    Task<CodeReference> ResolveCodeReferenceAsync(
        string reference,
        ReferenceResolutionOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CodeReference>> FindMatchingFilesAsync(
        string description,
        ReferenceResolutionOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<ReferenceIndex> BuildReferenceIndexAsync(
        string projectRoot,
        CancellationToken cancellationToken = default);
}
