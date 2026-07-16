using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

public abstract class AttributeRegistrationGeneratorBase<TInfo> : IIncrementalGenerator
{
    protected abstract string AttributeFullName { get; }

    protected abstract TInfo? ExtractInfo(INamedTypeSymbol typeSymbol, AttributeData attr, Compilation compilation);

    protected abstract void GenerateRegistration(SourceProductionContext context, ImmutableArray<TInfo> infos);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var infos = context.CompilationProvider
            .SelectMany((compilation, _) =>
            {
                var types = AttributeScanner.ScanTypesWithAttribute(compilation, AttributeFullName);
                var attrSymbol = compilation.GetTypeByMetadataName(AttributeFullName);
                if (attrSymbol is null)
                    return ImmutableArray<TInfo>.Empty;

                var results = new List<TInfo>();
                foreach (var typeSymbol in types)
                {
                    var attr = AttributeScanner.GetAttribute(typeSymbol, attrSymbol);
                    if (attr is null) continue;

                    var info = ExtractInfo(typeSymbol, attr, compilation);
                    if (info is not null)
                        results.Add(info);
                }
                return results.ToImmutableArray();
            })
            .Collect();

        context.RegisterSourceOutput(infos, static (ctx, infos) =>
        {
            GenerateRegistration(ctx, infos);
        });
    }
}
