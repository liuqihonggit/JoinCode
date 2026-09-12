namespace Fsm.Generator.Tests;

using JoinCode.Abstractions.Attributes;

/// <summary>
/// 源码生成器运行结果
/// </summary>
internal sealed class GeneratorRunResult
{
    public required string GeneratedCode { get; init; }
    public required GeneratorDriverRunResult RunResult { get; init; }
    public required CSharpCompilation Compilation { get; init; }
}

/// <summary>
/// 源码生成器测试辅助 — 构建 CSharpCompilation + 运行 FsmGenerator + 返回生成代码
/// </summary>
internal static class TestHelper
{
    /// <summary>
    /// 运行 FsmGenerator 并返回运行结果
    /// </summary>
    public static GeneratorRunResult RunGenerator(string source)
    {
        var compilation = CreateCompilation(source);
        var generator = new FsmGenerator();
        var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        var sb = new StringBuilder();
        foreach (var tree in runResult.GeneratedTrees)
            sb.AppendLine(tree.GetText().ToString());
        return new GeneratorRunResult { GeneratedCode = sb.ToString(), RunResult = runResult, Compilation = compilation };
    }

    /// <summary>
    /// 运行 FsmGenerator 并返回指定提示文件名的生成代码
    /// </summary>
    public static string? RunGeneratorAndGetFile(string source, string hintName)
    {
        var result = RunGenerator(source);
        foreach (var tree in result.RunResult.GeneratedTrees)
        {
            if (tree.FilePath.EndsWith(hintName, StringComparison.OrdinalIgnoreCase))
                return tree.GetText().ToString();
        }
        return null;
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var references = new List<MetadataReference>();
        var loadedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddAssemblyReferences(typeof(object).Assembly, references, loadedPaths);
        AddAssemblyReferences(typeof(FsmStateMachineAttribute).Assembly, references, loadedPaths);
        AddAssemblyReferences(typeof(Console).Assembly, references, loadedPaths);
        AddAssemblyReferences(typeof(System.Runtime.Loader.AssemblyLoadContext).Assembly, references, loadedPaths);
        AddAssemblyReferences(typeof(Enumerable).Assembly, references, loadedPaths);

        foreach (var asm in System.Runtime.Loader.AssemblyLoadContext.Default.Assemblies)
        {
            if (string.IsNullOrEmpty(asm.Location))
                continue;
            AddMetadataSafe(asm.Location, references, loadedPaths);
        }

        var syntaxTree = CSharpSyntaxTree.ParseText(SourceText.From(source));
        var compilation = CSharpCompilation.Create(
            "FsmGeneratorTestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        return compilation;
    }

    private static void AddAssemblyReferences(Assembly root, List<MetadataReference> references, HashSet<string> loadedPaths)
    {
        var queue = new Queue<Assembly>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var asm = queue.Dequeue();
            if (!AddMetadataSafe(asm.Location, references, loadedPaths))
                continue;
            foreach (var refName in asm.GetReferencedAssemblies())
            {
                try
                {
                    var loaded = Assembly.Load(refName);
                    if (loadedPaths.Contains(loaded.Location))
                        continue;
                    queue.Enqueue(loaded);
                }
                catch { }
            }
        }
    }

    private static bool AddMetadataSafe(string path, List<MetadataReference> references, HashSet<string> loadedPaths)
    {
        if (string.IsNullOrEmpty(path) || !loadedPaths.Add(path))
            return false;
        try
        {
            references.Add(MetadataReference.CreateFromFile(path));
            return true;
        }
        catch
        {
            loadedPaths.Remove(path);
            return false;
        }
    }
}
