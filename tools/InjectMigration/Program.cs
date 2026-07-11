using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text;

if (args.Length < 1)
{
    Console.WriteLine("Usage: InjectMigration <directory> [--dry-run]");
    return;
}

var rootDir = args[0];
var dryRun = args.Contains("--dry-run");
var csFiles = Directory.GetFiles(rootDir, "*.cs", SearchOption.AllDirectories)
    .Where(f => !f.Contains("\\obj\\") && !f.Contains("\\bin\\") && !f.Contains("\\.x\\") && !f.Contains("\\tests\\"))
    .ToList();

Console.WriteLine($"Scanning {csFiles.Count} files in {rootDir}...");

var totalUpdated = 0;
var totalSkipped = 0;
var totalErrors = 0;

// DI类型后缀列表
var diSuffixes = new HashSet<string>
{
    "Service", "Manager", "Provider", "Factory", "Handler", "Client",
    "Repository", "Store", "Builder", "Resolver", "Converter", "Serializer",
    "Validator", "Analyzer", "Collector", "Monitor", "Tracker", "Processor",
    "Engine", "Adapter", "Wrapper", "Helper", "Registry", "Dispatcher",
    "Executor", "Interceptor", "Middleware", "Router", "Gateway", "Hub",
    "Channel", "Pool", "Cache", "Strategy", "Policy", "Context", "Coordinator",
    "Scheduler", "Observer", "Listener", "Notifier", "Publisher", "Subscriber",
    "Checker", "Walker", "Detector", "Scorer", "Selector", "Comparer",
    "Filter", "Formatter", "Parser", "Renderer", "Encoder", "Decoder"
};

// 已知DI具体类
var knownDIConcreteTypes = new HashSet<string>
{
    "CostTracker", "WebFetchCache", "SystemPromptBuilder",
    "ToolIdleReminderService", "SystemReminderManager"
};

// 非DI原始类型
var primitiveTypes = new HashSet<string>
{
    "string", "int", "bool", "double", "float", "long", "byte", "char",
    "decimal", "DateTime", "TimeSpan", "Guid", "object", "CancellationToken",
    "Version", "Stream", "Type", "Action", "Func", "Task", "ValueTask"
};

foreach (var file in csFiles)
{
    try
    {
        var source = File.ReadAllText(file);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        // 只处理有 [Register] 特性的文件
        if (!source.Contains("[Register"))
            continue;

        var rewriter = new InjectMigrationRewriter(diSuffixes, knownDIConcreteTypes, primitiveTypes);
        var newRoot = rewriter.Visit(root);

        if (newRoot != root)
        {
            if (dryRun)
            {
                Console.WriteLine($"[DRY-RUN] Would update: {Path.GetRelativePath(rootDir, file)}");
            }
            else
            {
                var newSource = newRoot.ToFullString();
                File.WriteAllText(file, newSource);
                Console.WriteLine($"Updated: {Path.GetRelativePath(rootDir, file)}");
            }
            totalUpdated++;
        }
        else
        {
            totalSkipped++;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR: {Path.GetRelativePath(rootDir, file)}: {ex.Message}");
        totalErrors++;
    }
}

Console.WriteLine($"\nDone. Updated: {totalUpdated}, Skipped: {totalSkipped}, Errors: {totalErrors}");

sealed class InjectMigrationRewriter(
    HashSet<string> diSuffixes,
    HashSet<string> knownDIConcreteTypes,
    HashSet<string> primitiveTypes) : CSharpSyntaxRewriter
{
    public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        // 只处理有 [Register] 特性的类
        var hasRegister = node.AttributeLists.Any(al => al.Attributes.Any(a => a.Name.ToString() == "Register"));
        if (!hasRegister)
            return base.VisitClassDeclaration(node);

        // 跳过 abstract/static 类
        if (node.Modifiers.Any(m => m.IsKind(SyntaxKind.AbstractKeyword) || m.IsKind(SyntaxKind.StaticKeyword)))
            return base.VisitClassDeclaration(node);

        // 找到唯一的构造函数
        var constructors = node.Members.OfType<ConstructorDeclarationSyntax>().ToList();
        if (constructors.Count == 0)
            return base.VisitClassDeclaration(node);

        // 跳过多构造函数
        if (constructors.Count > 1)
            return base.VisitClassDeclaration(node);

        var ctor = constructors[0];
        if (ctor.ParameterList.Parameters.Count == 0)
            return base.VisitClassDeclaration(node);

        // 检查所有参数是否都是DI类型
        var allDI = true;
        foreach (var param in ctor.ParameterList.Parameters)
        {
            if (!IsDIType(param.Type))
            {
                allDI = false;
                break;
            }
        }

        if (!allDI)
            return base.VisitClassDeclaration(node);

        // 检查构造函数体是否只有简单赋值
        var body = ctor.Body;
        if (body is null)
            return base.VisitClassDeclaration(node);

        foreach (var stmt in body.Statements)
        {
            if (stmt is ExpressionStatementSyntax ess && ess.Expression is AssignmentExpressionSyntax aes)
            {
                // 允许: _field = param;
                // 允许: _field = param ?? throw ...;
                // 允许: ArgumentNullException.ThrowIfNull(param);
                continue;
            }
            if (stmt is ExpressionStatementSyntax ess2 && ess2.Expression is InvocationExpressionSyntax)
            {
                // 允许: ArgumentNullException.ThrowIfNull(param);
                var expr = ess2.Expression.ToString();
                if (expr.StartsWith("ArgumentNullException.ThrowIfNull") || expr.StartsWith("ArgumentException.ThrowIfNullOrEmpty"))
                    continue;
            }
            // 不允许其他语句
            allDI = false;
            break;
        }

        if (!allDI)
            return base.VisitClassDeclaration(node);

        // 可以删除构造函数！
        // 1. 给所有构造函数参数对应的字段加 [Inject]
        // 2. 类声明加 partial
        // 3. 删除构造函数

        var existingInjectFields = node.Members.OfType<FieldDeclarationSyntax>()
            .Where(f => f.AttributeLists.Any(al => al.Attributes.Any(a => a.Name.ToString() == "Inject")))
            .Select(f => f.Declaration.Variables.First().Identifier.Text)
            .ToHashSet();

        var ctorParamNames = ctor.ParameterList.Parameters
            .Select(p => p.Identifier.Text)
            .ToHashSet();

        var newMembers = new List<MemberDeclarationSyntax>();

        foreach (var member in node.Members)
        {
            if (member == ctor)
                continue; // 删除构造函数

            if (member is FieldDeclarationSyntax field)
            {
                var fieldName = field.Declaration.Variables.First().Identifier.Text;
                // 检查字段名是否匹配构造函数参数名（_paramName 或 paramName）
                var matchingParam = ctorParamNames.FirstOrDefault(pn => fieldName == "_" + pn || fieldName == pn);
                if (matchingParam != null && !existingInjectFields.Contains(fieldName))
                {
                    // 给字段添加 [Inject] 特性
                    var injectAttr = SyntaxFactory.Attribute(SyntaxFactory.IdentifierName("Inject"));
                    var attrList = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(injectAttr));
                    var newField = field.AddAttributeLists(attrList);
                    newMembers.Add(newField);
                    continue;
                }
            }

            newMembers.Add(member);
        }

        // 类声明加 partial
        var newClass = node.WithMembers(new SyntaxList<MemberDeclarationSyntax>(newMembers));

        if (!newClass.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
        {
            newClass = newClass.AddModifiers(SyntaxFactory.Token(SyntaxKind.PartialKeyword));
        }

        return base.VisitClassDeclaration(newClass);
    }

    private bool IsDIType(TypeSyntax? type)
    {
        if (type is null) return false;

        var typeName = type.ToString()
            .Replace("?", "")
            .Trim();

        // 移除泛型参数
        var genericIdx = typeName.IndexOf('<');
        if (genericIdx > 0)
            typeName = typeName[..genericIdx];

        // 移除数组标记
        typeName = typeName.TrimEnd('[', ']');

        // 检查原始类型
        if (primitiveTypes.Contains(typeName))
            return false;

        // 检查集合类型
        if (typeName is "List" or "Dictionary" or "HashSet" or "Queue" or "Stack"
            or "ReadOnlyMemory" or "ImmutableArray" or "ImmutableDictionary"
            or "ConcurrentDictionary" or "IReadOnlyList" or "IReadOnlyDictionary"
            or "IEnumerable" or "ICollection" or "IList")
            return false;

        // 检查接口（以I开头+大写字母）
        if (typeName.Length > 1 && typeName[0] == 'I' && char.IsUpper(typeName[1]))
            return true;

        // 检查 ILogger
        if (typeName == "ILogger")
            return true;

        // 检查 IOptions
        if (typeName == "IOptions" || typeName == "IOptionsMonitor" || typeName == "IOptionsSnapshot")
            return true;

        // 检查 IServiceScopeFactory 等
        if (typeName.StartsWith("IService"))
            return true;

        // 检查已知DI具体类
        if (knownDIConcreteTypes.Contains(typeName))
            return true;

        // 检查DI后缀
        foreach (var suffix in diSuffixes)
        {
            if (typeName.EndsWith(suffix))
                return true;
        }

        return false;
    }
}
