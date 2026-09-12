
namespace McpToolDispatch.Generator;

[Generator]
public sealed class CommandRegistrationGenerator : IIncrementalGenerator
{
    private const string ChatCommandAttributeFullName = "JoinCode.ChatCommands.ChatCommandAttribute";
    private const string ChatCommandArgAttributeFullName = "JoinCode.ChatCommands.ChatCommandArgAttribute";
    private const string IChatCommandFullName = "JoinCode.ChatCommands.IChatCommand";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var commandTypes = context.CompilationProvider
            .SelectMany(static (compilation, _) =>
            {
                var chatCommandAttr = compilation.GetTypeByMetadataName(ChatCommandAttributeFullName);

                if (chatCommandAttr is null)
                    return ImmutableArray<CommandInfo>.Empty;

                var chatCommandArgAttr = compilation.GetTypeByMetadataName(ChatCommandArgAttributeFullName);
                var results = new List<CommandInfo>();
                VisitNamespaces(compilation.GlobalNamespace, chatCommandAttr, chatCommandArgAttr, results);
                return results.ToImmutableArray();
            })
            .Collect();

        context.RegisterSourceOutput(commandTypes, static (ctx, commands) =>
        {
            GenerateRegistrationCode(ctx, commands);
            GenerateSlashCommandCatalog(ctx, commands);
            GenerateSlashCommandSchemaCatalog(ctx, commands);
        });
    }

    private static void VisitNamespaces(
        INamespaceSymbol namespaceSymbol,
        INamedTypeSymbol? chatCommandAttr,
        INamedTypeSymbol? chatCommandArgAttr,
        List<CommandInfo> results)
    {
        foreach (var member in namespaceSymbol.GetMembers())
        {
            if (member is INamespaceSymbol childNamespace)
                VisitNamespaces(childNamespace, chatCommandAttr, chatCommandArgAttr, results);
            else if (member is INamedTypeSymbol typeSymbol && chatCommandAttr is not null)
            {
                if (!typeSymbol.Locations.Any(static loc => loc.IsInSource))
                    continue;

                var attr = typeSymbol.GetAttributes()
                    .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, chatCommandAttr));
                if (attr is not null)
                {
                    var name = attr.NamedArguments.FirstOrDefault(n => n.Key == "Name").Value.Value as string
                        ?? attr.ConstructorArguments.ElementAtOrDefault(0).Value as string
                        ?? typeSymbol.Name;

                    // 提取 Category — 特性解耦，每个命令自己声明分类
                    var categoryValue = attr.NamedArguments.FirstOrDefault(n => n.Key == "Category").Value;
                    string? categoryEnumName = null;
                    if (!categoryValue.IsNull)
                    {
                        // 从枚举值获取成员名（如 ChatCommandCategory.Session → "Session"）
                        if (categoryValue.Value is int categoryInt && categoryInt >= 0)
                        {
                            // 遍历枚举成员找到匹配的名称
                            var categoryType = categoryValue.Type;
                            if (categoryType is INamedTypeSymbol enumType)
                            {
                                foreach (var field in enumType.GetMembers().OfType<IFieldSymbol>())
                                {
                                    if (field.ConstantValue is int fieldValue && fieldValue == categoryInt)
                                    {
                                        categoryEnumName = field.Name;
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    var description = attr.NamedArguments.FirstOrDefault(n => n.Key == "Description").Value.Value as string ?? "";
                    var usage = attr.NamedArguments.FirstOrDefault(n => n.Key == "Usage").Value.Value as string ?? "";
                    var argumentHint = attr.NamedArguments.FirstOrDefault(n => n.Key == "ArgumentHint").Value.Value as string ?? "";
                    var isHidden = attr.NamedArguments.FirstOrDefault(n => n.Key == "IsHidden").Value.Value is bool hiddenVal && hiddenVal;
                    var isEnabled = attr.NamedArguments.FirstOrDefault(n => n.Key == "IsEnabled").Value.Value is bool enabledVal ? enabledVal : true;
                    var aliases = new List<string>();
                    var aliasesValue = attr.NamedArguments.FirstOrDefault(n => n.Key == "Aliases").Value;
                    if (!aliasesValue.IsNull && aliasesValue.Kind == TypedConstantKind.Array)
                    {
                        foreach (var element in aliasesValue.Values)
                        {
                            if (element.Value is string alias)
                                aliases.Add(alias);
                        }
                    }

                    var args = new List<ChatCommandArgInfo>();
                    if (chatCommandArgAttr is not null)
                    {
                        foreach (var a in typeSymbol.GetAttributes())
                        {
                            if (SymbolEqualityComparer.Default.Equals(a.AttributeClass, chatCommandArgAttr))
                                args.Add(ExtractArgInfo(a));
                        }
                    }

                    results.Add(new CommandInfo(
                        typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        name,
                        CommandType.ChatCommand,
                        categoryEnumName,
                        description,
                        usage,
                        aliases.ToArray(),
                        isHidden,
                        isEnabled,
                        argumentHint,
                        args.ToArray()));
                }
            }
        }
    }

    private static ChatCommandArgInfo ExtractArgInfo(AttributeData a)
    {
        // Name 是构造函数参数，Type/Description/Required 等是 init 命名参数
        var argName = a.ConstructorArguments.ElementAtOrDefault(0).Value as string ?? "";
        var argType = a.NamedArguments.FirstOrDefault(n => n.Key == "Type").Value.Value as string ?? "string";
        var argDesc = a.NamedArguments.FirstOrDefault(n => n.Key == "Description").Value.Value as string ?? "";
        var argRequired = a.NamedArguments.FirstOrDefault(n => n.Key == "Required").Value.Value is bool reqVal && reqVal;
        var argDefault = a.NamedArguments.FirstOrDefault(n => n.Key == "Default").Value.Value as string;
        var argItemsType = a.NamedArguments.FirstOrDefault(n => n.Key == "ItemsType").Value.Value as string;
        var argItemsDesc = a.NamedArguments.FirstOrDefault(n => n.Key == "ItemsDescription").Value.Value as string;

        string[]? argEnum = null;
        var enumValue = a.NamedArguments.FirstOrDefault(n => n.Key == "Enum").Value;
        if (!enumValue.IsNull && enumValue.Kind == TypedConstantKind.Array)
        {
            var enumList = new List<string>();
            foreach (var element in enumValue.Values)
            {
                if (element.Value is string enumItem)
                    enumList.Add(enumItem);
            }
            argEnum = enumList.ToArray();
        }

        return new ChatCommandArgInfo(argName, argType, argDesc, argRequired, argDefault, argEnum, argItemsType, argItemsDesc);
    }

    private static void GenerateRegistrationCode(SourceProductionContext context, ImmutableArray<CommandInfo> commands)
    {
        var validCommands = commands.OrderBy(c => c.Name).ToList();

        if (validCommands.Count == 0)
            return;

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using JoinCode.ChatCommands;");
        sb.AppendLine();
        sb.AppendLine("namespace JoinCode;");
        sb.AppendLine();
        sb.AppendLine("public static partial class GeneratedCommandRegistration");
        sb.AppendLine("{");

        var chatCommands = validCommands.Where(c => c.Type == CommandType.ChatCommand).ToList();

        if (chatCommands.Count > 0)
        {
            GenerateRegisterChatCommandsMethod(sb, chatCommands);
            sb.AppendLine();
        }

        sb.AppendLine("}");

        context.AddSource("GeneratedCommandRegistration.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static void GenerateRegisterChatCommandsMethod(StringBuilder sb, List<CommandInfo> commands)
    {
        sb.AppendLine("    public static void RegisterAllChatCommands(ChatCommandRegistry registry)");
        sb.AppendLine("    {");

        foreach (var cmd in commands)
        {
            sb.AppendLine($"        registry.Register(new {cmd.FullyQualifiedName}());");

            // 生成 SetCategory 调用 — 特性解耦，源码生成器自动提取
            if (cmd.CategoryEnumName is not null)
            {
                sb.AppendLine($"        registry.SetCategory(\"{EscapeString(cmd.Name)}\", JoinCode.Abstractions.Utils.ChatCommandCategory.{cmd.CategoryEnumName});");
            }
        }

        sb.AppendLine("    }");
    }

    /// <summary>生成 GeneratedSlashCommandCatalog — 从 [ChatCommand] 特性提取的命令元数据目录</summary>
    private static void GenerateSlashCommandCatalog(SourceProductionContext context, ImmutableArray<CommandInfo> commands)
    {
        var chatCommands = commands.Where(c => c.Type == CommandType.ChatCommand).OrderBy(c => c.Name).ToList();
        if (chatCommands.Count == 0)
            return;

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using JoinCode.Abstractions.Interfaces;");
        sb.AppendLine();
        sb.AppendLine("namespace JoinCode.ChatCommands;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// 斜杠命令目录 — 由源码生成器从 [ChatCommand] 特性自动提取，实现 ISlashCommandCatalog。");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public sealed class GeneratedSlashCommandCatalog : ISlashCommandCatalog");
        sb.AppendLine("{");
        sb.AppendLine("    public IReadOnlyList<SlashCommandMetadata> Commands { get; } =");
        sb.AppendLine("    [");

        foreach (var cmd in chatCommands)
        {
            var aliases = string.Join(", ", cmd.Aliases.Select(a => $"\"{EscapeString(a)}\""));
            sb.AppendLine($"        new SlashCommandMetadata {{ Name = \"/{EscapeString(cmd.Name)}\", Description = \"{EscapeString(cmd.Description)}\", Usage = \"{EscapeString(cmd.Usage)}\", Aliases = [{aliases}], IsHidden = {cmd.IsHidden.ToString().ToLowerInvariant()}, IsEnabled = {cmd.IsEnabled.ToString().ToLowerInvariant()}, Category = \"{EscapeString(cmd.CategoryEnumName ?? "Other")}\" }},");
        }

        sb.AppendLine("    ];");
        sb.AppendLine("}");

        context.AddSource("GeneratedSlashCommandCatalog.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    /// <summary>生成 GeneratedSlashCommandSchemaCatalog — 从 [ChatCommandArg] 特性提取的参数 schema 目录</summary>
    private static void GenerateSlashCommandSchemaCatalog(SourceProductionContext context, ImmutableArray<CommandInfo> commands)
    {
        var chatCommands = commands.Where(c => c.Type == CommandType.ChatCommand).OrderBy(c => c.Name).ToList();
        if (chatCommands.Count == 0)
            return;

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using JoinCode.Abstractions.Interfaces;");
        sb.AppendLine("using JoinCode.Abstractions.Tools;");
        sb.AppendLine();
        sb.AppendLine("namespace JoinCode.ChatCommands;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// 斜杠命令参数 schema 目录 — 由源码生成器从 [ChatCommandArg] 特性自动提取，实现 ISlashCommandSchemaCatalog。");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public sealed class GeneratedSlashCommandSchemaCatalog : ISlashCommandSchemaCatalog");
        sb.AppendLine("{");
        sb.AppendLine("    public IReadOnlyList<SlashCommandSchemaEntry> AllSchemas { get; } =");
        sb.AppendLine("    [");

        foreach (var cmd in chatCommands)
        {
            if (cmd.Args.Length > 0)
            {
                sb.AppendLine($"        new SlashCommandSchemaEntry {{ CommandName = \"{EscapeString(cmd.Name)}\", Schema = {GenerateToolSchemaLiteral(cmd.Args)} }},");
            }
            else
            {
                var hint = string.IsNullOrEmpty(cmd.ArgumentHint) ? "null" : $"\"{EscapeString(cmd.ArgumentHint)}\"";
                sb.AppendLine($"        new SlashCommandSchemaEntry {{ CommandName = \"{EscapeString(cmd.Name)}\", Schema = null, ArgumentHint = {hint} }},");
            }
        }

        sb.AppendLine("    ];");
        sb.AppendLine();
        sb.AppendLine("    public ToolSchema? GetSchema(string commandName)");
        sb.AppendLine("    {");
        sb.AppendLine("        foreach (var entry in AllSchemas)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (entry.CommandName == commandName)");
        sb.AppendLine("                return entry.Schema;");
        sb.AppendLine("        }");
        sb.AppendLine("        return null;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        context.AddSource("GeneratedSlashCommandSchemaCatalog.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static string GenerateToolSchemaLiteral(ChatCommandArgInfo[] args)
    {
        var sb = new StringBuilder();
        sb.Append("new ToolSchema { Properties = new() {");

        var first = true;
        var required = new List<string>();

        foreach (var arg in args)
        {
            if (!first) sb.Append(",");
            first = false;

            sb.Append($" [\"{EscapeString(arg.Name)}\"] = new ToolSchemaProperty {{ Type = \"{EscapeString(arg.Type)}\", Description = \"{EscapeString(arg.Description)}\"");

            if (arg.Enum is not null && arg.Enum.Length > 0)
            {
                var enumItems = string.Join(", ", arg.Enum.Select(e => $"\"{EscapeString(e)}\""));
                sb.Append($", Enum = [{enumItems}]");
            }

            if (arg.Default is not null)
                sb.Append($", Default = \"{EscapeString(arg.Default)}\"");

            if (arg.ItemsType is not null)
            {
                sb.Append($", Items = new ToolSchemaProperty {{ Type = \"{EscapeString(arg.ItemsType)}\"");
                if (arg.ItemsDescription is not null)
                    sb.Append($", Description = \"{EscapeString(arg.ItemsDescription)}\"");
                sb.Append(" }");
            }

            sb.Append(" }");

            if (arg.Required)
                required.Add(arg.Name);
        }

        sb.Append(" }");

        if (required.Count > 0)
        {
            var reqList = string.Join(", ", required.Select(r => $"\"{EscapeString(r)}\""));
            sb.Append($", Required = [{reqList}]");
        }

        sb.Append(" }");
        return sb.ToString();
    }

    private static string EscapeString(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private enum CommandType
    {
        ChatCommand
    }

    private sealed class CommandInfo
    {
        public string FullyQualifiedName { get; }
        public string Name { get; }
        public CommandType Type { get; }
        public string? CategoryEnumName { get; }
        public string Description { get; }
        public string Usage { get; }
        public string[] Aliases { get; }
        public bool IsHidden { get; }
        public bool IsEnabled { get; }
        public string ArgumentHint { get; }
        public ChatCommandArgInfo[] Args { get; }

        public CommandInfo(
            string fullyQualifiedName,
            string name,
            CommandType type,
            string? categoryEnumName = null,
            string description = "",
            string usage = "",
            string[]? aliases = null,
            bool isHidden = false,
            bool isEnabled = true,
            string argumentHint = "",
            ChatCommandArgInfo[]? args = null)
        {
            FullyQualifiedName = fullyQualifiedName;
            Name = name;
            Type = type;
            CategoryEnumName = categoryEnumName;
            Description = description;
            Usage = usage;
            Aliases = aliases ?? [];
            IsHidden = isHidden;
            IsEnabled = isEnabled;
            ArgumentHint = argumentHint;
            Args = args ?? [];
        }
    }

    private sealed class ChatCommandArgInfo
    {
        public string Name { get; }
        public string Type { get; }
        public string Description { get; }
        public bool Required { get; }
        public string? Default { get; }
        public string[]? Enum { get; }
        public string? ItemsType { get; }
        public string? ItemsDescription { get; }

        public ChatCommandArgInfo(
            string name,
            string type,
            string description,
            bool required,
            string? defaultVal,
            string[]? enumVal,
            string? itemsType,
            string? itemsDescription)
        {
            Name = name;
            Type = type;
            Description = description;
            Required = required;
            Default = defaultVal;
            Enum = enumVal;
            ItemsType = itemsType;
            ItemsDescription = itemsDescription;
        }
    }
}
