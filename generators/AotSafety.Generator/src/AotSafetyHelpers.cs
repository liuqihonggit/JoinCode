namespace AotSafety.Generator
{
    /// <summary>
    /// AOT 安全分析器共享辅助方法
    /// </summary>
    public static class AotSafetyHelpers
    {
        /// <summary>
        /// 检查节点是否在循环内（for/while/do/foreach）
        /// </summary>
        public static bool IsInsideLoop(SyntaxNode node)
        {
            var current = node.Parent;
            while (current is not null)
            {
                if (current is ForStatementSyntax or WhileStatementSyntax or DoStatementSyntax or ForEachStatementSyntax or ForEachVariableStatementSyntax)
                    return true;
                current = current.Parent;
            }
            return false;
        }

        /// <summary>
        /// 判断是否在测试方法内
        /// </summary>
        public static bool IsInsideTestMethod(SyntaxNode node)
        {
            var current = node.Parent;
            while (current is not null)
            {
                if (current is MethodDeclarationSyntax methodDecl)
                {
                    if (methodDecl.AttributeLists.Any(al =>
                        al.Attributes.Any(a =>
                        {
                            var name = a.Name.ToString();
                            return name == "Fact" || name == "Theory" || name == "TestMethod" ||
                                   name == "Test" || name == "InlineData" ||
                                   name.Contains("Fact", StringComparison.Ordinal) ||
                                   name.Contains("Test", StringComparison.Ordinal);
                        })))
                        return true;
                }
                current = current.Parent;
            }
            return false;
        }

        /// <summary>
        /// 查找包含指定节点的类型声明
        /// </summary>
        public static TypeDeclarationSyntax? FindEnclosingTypeDeclaration(SyntaxNode node)
        {
            var current = node.Parent;
            while (current is not null)
            {
                if (current is TypeDeclarationSyntax typeDecl)
                    return typeDecl;
                current = current.Parent;
            }
            return null;
        }

        /// <summary>
        /// 找到包含指定节点的最近方法声明
        /// </summary>
        public static MethodDeclarationSyntax? FindEnclosingMethodDeclaration(SyntaxNode node)
        {
            var current = node.Parent;
            while (current is not null)
            {
                if (current is MethodDeclarationSyntax method)
                    return method;
                current = current.Parent;
            }
            return null;
        }
    }
}
