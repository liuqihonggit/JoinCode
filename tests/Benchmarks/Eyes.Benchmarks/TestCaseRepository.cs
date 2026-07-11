namespace CodeIndex.Benchmarks;

public static class TestCaseRepository
{
    public static List<TestCase> GetL1TestCases() =>
    [
        .. ExactSearchCases(),
        .. FuzzySearchCases(),
        .. TypeSearchCases(),
        .. CrossFileSearchCases()
    ];

    public static List<TestCase> GetL2TestCases() =>
    [
        .. CallerCalleeCases(),
        .. CallChainCases(),
        .. ImpactScopeCases(),
        .. InheritanceCases()
    ];

    private static List<TestCase> ExactSearchCases() =>
    [
        new() { Id = "L1-001", Category = "exact_search", ExpectedLayer = "L1", Query = "UserService", ExpectedResults = ["UserService"], Description = "精确搜索类名 UserService" },
        new() { Id = "L1-002", Category = "exact_search", ExpectedLayer = "L1", Query = "GetUser", ExpectedResults = ["GetUser"], Description = "精确搜索方法名 GetUser" },
        new() { Id = "L1-003", Category = "exact_search", ExpectedLayer = "L1", Query = "UserId", ExpectedResults = ["UserId"], Description = "精确搜索属性名 UserId" },
        new() { Id = "L1-004", Category = "exact_search", ExpectedLayer = "L1", Query = "OrderService", ExpectedResults = ["OrderService"], Description = "精确搜索类名 OrderService" },
        new() { Id = "L1-005", Category = "exact_search", ExpectedLayer = "L1", Query = "CreateOrder", ExpectedResults = ["CreateOrder"], Description = "精确搜索方法名 CreateOrder" },
        new() { Id = "L1-006", Category = "exact_search", ExpectedLayer = "L1", Query = "IRepository", ExpectedResults = ["IRepository"], Description = "精确搜索接口名 IRepository" },
        new() { Id = "L1-007", Category = "exact_search", ExpectedLayer = "L1", Query = "GetById", ExpectedResults = ["GetById"], Description = "精确搜索方法名 GetById" },
        new() { Id = "L1-008", Category = "exact_search", ExpectedLayer = "L1", Query = "Save", ExpectedResults = ["Save"], Description = "精确搜索方法名 Save" },
        new() { Id = "L1-009", Category = "exact_search", ExpectedLayer = "L1", Query = "Calculator", ExpectedResults = ["Calculator"], Description = "精确搜索类名 Calculator" },
        new() { Id = "L1-010", Category = "exact_search", ExpectedLayer = "L1", Query = "Compute", ExpectedResults = ["Compute"], Description = "精确搜索方法名 Compute" },
        new() { Id = "L1-011", Category = "exact_search", ExpectedLayer = "L1", Query = "Helper", ExpectedResults = ["Helper"], Description = "精确搜索静态类 Helper" },
        new() { Id = "L1-012", Category = "exact_search", ExpectedLayer = "L1", Query = "Square", ExpectedResults = ["Square"], Description = "精确搜索方法名 Square" },
        new() { Id = "L1-013", Category = "exact_search", ExpectedLayer = "L1", Query = "Controller", ExpectedResults = ["Controller"], Description = "精确搜索类名 Controller" },
        new() { Id = "L1-014", Category = "exact_search", ExpectedLayer = "L1", Query = "Handle", ExpectedResults = ["Handle"], Description = "精确搜索方法名 Handle" },
        new() { Id = "L1-015", Category = "exact_search", ExpectedLayer = "L1", Query = "ValidateUser", ExpectedResults = ["ValidateUser"], Description = "精确搜索方法名 ValidateUser" }
    ];

    private static List<TestCase> FuzzySearchCases() =>
    [
        new() { Id = "L1-016", Category = "fuzzy_search", ExpectedLayer = "L1", Query = "User*", ExpectedResults = ["UserService", "UserId", "UserName", "GetUser", "ValidateUser"], Description = "模糊搜索 User* 前缀" },
        new() { Id = "L1-017", Category = "fuzzy_search", ExpectedLayer = "L1", Query = "*Service", ExpectedResults = ["UserService", "OrderService"], Description = "模糊搜索 *Service 后缀" },
        new() { Id = "L1-018", Category = "fuzzy_search", ExpectedLayer = "L1", Query = "Get*", ExpectedResults = ["GetUser", "GetById", "GetAll", "GetDisplayName"], Description = "模糊搜索 Get* 前缀" },
        new() { Id = "L1-019", Category = "fuzzy_search", ExpectedLayer = "L1", Query = "*Order", ExpectedResults = ["OrderService", "CreateOrder"], Description = "模糊搜索 *Order 后缀" },
        new() { Id = "L1-020", Category = "fuzzy_search", ExpectedLayer = "L1", Query = "Repos*", ExpectedResults = ["Repository", "IRepository"], Description = "模糊搜索 Repos* 前缀" },
        new() { Id = "L1-021", Category = "fuzzy_search", ExpectedLayer = "L1", Query = "*All", ExpectedResults = ["GetAll"], Description = "模糊搜索 *All 后缀" },
        new() { Id = "L1-022", Category = "fuzzy_search", ExpectedLayer = "L1", Query = "Cal*", ExpectedResults = ["Calculator", "Compute"], Description = "模糊搜索 Cal* 前缀" },
        new() { Id = "L1-023", Category = "fuzzy_search", ExpectedLayer = "L1", Query = "*Name", ExpectedResults = ["UserName", "GetName", "GetDisplayName"], Description = "模糊搜索 *Name 后缀" },
        new() { Id = "L1-024", Category = "fuzzy_search", ExpectedLayer = "L1", Query = "I*Repository", ExpectedResults = ["IRepository"], Description = "模糊搜索 I*Repository 模式" },
        new() { Id = "L1-025", Category = "fuzzy_search", ExpectedLayer = "L1", Query = "*Helper", ExpectedResults = ["Helper"], Description = "模糊搜索 *Helper 后缀" },
        new() { Id = "L1-026", Category = "fuzzy_search", ExpectedLayer = "L1", Query = "Cont*", ExpectedResults = ["Controller"], Description = "模糊搜索 Cont* 前缀" },
        new() { Id = "L1-027", Category = "fuzzy_search", ExpectedLayer = "L1", Query = "*Valid*", ExpectedResults = ["ValidateUser"], Description = "模糊搜索 *Valid* 包含" },
        new() { Id = "L1-028", Category = "fuzzy_search", ExpectedLayer = "L1", Query = "Save", ExpectedResults = ["Save"], Description = "搜索 Save 方法" },
        new() { Id = "L1-029", Category = "fuzzy_search", ExpectedLayer = "L1", Query = "*Display*", ExpectedResults = ["GetDisplayName"], Description = "模糊搜索 *Display* 包含" },
        new() { Id = "L1-030", Category = "fuzzy_search", ExpectedLayer = "L1", Query = "Squ*", ExpectedResults = ["Square"], Description = "模糊搜索 Squ* 前缀" }
    ];

    private static List<TestCase> TypeSearchCases() =>
    [
        new() { Id = "L1-031", Category = "type_search", ExpectedLayer = "L1", Query = "class", ExpectedResults = [], Description = "按类型搜索 class", QueryType = "class" },
        new() { Id = "L1-032", Category = "type_search", ExpectedLayer = "L1", Query = "interface", ExpectedResults = [], Description = "按类型搜索 interface", QueryType = "interface" },
        new() { Id = "L1-033", Category = "type_search", ExpectedLayer = "L1", Query = "method", ExpectedResults = [], Description = "按类型搜索 method", QueryType = "method" },
        new() { Id = "L1-034", Category = "type_search", ExpectedLayer = "L1", Query = "property", ExpectedResults = [], Description = "按类型搜索 property", QueryType = "property" },
        new() { Id = "L1-035", Category = "type_search", ExpectedLayer = "L1", Query = "constructor", ExpectedResults = [], Description = "按类型搜索 constructor", QueryType = "constructor" },
        new() { Id = "L1-036", Category = "type_search", ExpectedLayer = "L1", Query = "UserService", ExpectedResults = ["UserService"], Description = "搜索类 UserService", QueryType = "class" },
        new() { Id = "L1-037", Category = "type_search", ExpectedLayer = "L1", Query = "IRepository", ExpectedResults = ["IRepository"], Description = "搜索接口 IRepository", QueryType = "interface" },
        new() { Id = "L1-038", Category = "type_search", ExpectedLayer = "L1", Query = "GetById", ExpectedResults = ["GetById"], Description = "搜索方法 GetById", QueryType = "method" },
        new() { Id = "L1-039", Category = "type_search", ExpectedLayer = "L1", Query = "UserId", ExpectedResults = ["UserId"], Description = "搜索属性 UserId", QueryType = "property" },
        new() { Id = "L1-040", Category = "type_search", ExpectedLayer = "L1", Query = "Helper", ExpectedResults = ["Helper"], Description = "搜索静态类 Helper", QueryType = "class" }
    ];

    private static List<TestCase> CrossFileSearchCases() =>
    [
        new() { Id = "L1-041", Category = "cross_file_search", ExpectedLayer = "L1", Query = "UserService", ExpectedResults = ["UserService"], Description = "跨文件搜索 UserService 定义和引用" },
        new() { Id = "L1-042", Category = "cross_file_search", ExpectedLayer = "L1", Query = "OrderService", ExpectedResults = ["OrderService"], Description = "跨文件搜索 OrderService" },
        new() { Id = "L1-043", Category = "cross_file_search", ExpectedLayer = "L1", Query = "Repository", ExpectedResults = ["Repository", "IRepository"], Description = "跨文件搜索 Repository 相关" },
        new() { Id = "L1-044", Category = "cross_file_search", ExpectedLayer = "L1", Query = "GetName", ExpectedResults = ["GetName"], Description = "跨文件搜索 GetName 定义" },
        new() { Id = "L1-045", Category = "cross_file_search", ExpectedLayer = "L1", Query = "Compute", ExpectedResults = ["Compute"], Description = "跨文件搜索 Compute 定义" },
        new() { Id = "L1-046", Category = "cross_file_search", ExpectedLayer = "L1", Query = "Square", ExpectedResults = ["Square"], Description = "跨文件搜索 Square 定义和引用" },
        new() { Id = "L1-047", Category = "cross_file_search", ExpectedLayer = "L1", Query = "Handle", ExpectedResults = ["Handle"], Description = "跨文件搜索 Handle 定义" },
        new() { Id = "L1-048", Category = "cross_file_search", ExpectedLayer = "L1", Query = "CreateOrder", ExpectedResults = ["CreateOrder"], Description = "跨文件搜索 CreateOrder 定义" },
        new() { Id = "L1-049", Category = "cross_file_search", ExpectedLayer = "L1", Query = "GetDisplayName", ExpectedResults = ["GetDisplayName"], Description = "跨文件搜索 GetDisplayName 定义" },
        new() { Id = "L1-050", Category = "cross_file_search", ExpectedLayer = "L1", Query = "GetAll", ExpectedResults = ["GetAll"], Description = "跨文件搜索 GetAll 定义" }
    ];

    private static List<TestCase> CallerCalleeCases() =>
    [
        new() { Id = "L2-001", Category = "caller_callee", ExpectedLayer = "L2", Query = "callers", SourceSymbol = "Helper.Square", ExpectedResults = ["Calculator.Compute→Helper.Square"], Description = "查找 Square 的调用者" },
        new() { Id = "L2-002", Category = "caller_callee", ExpectedLayer = "L2", Query = "callees", SourceSymbol = "Calculator.Compute", ExpectedResults = ["Calculator.Compute→Helper.Square"], Description = "查找 Compute 的被调用者" },
        new() { Id = "L2-003", Category = "caller_callee", ExpectedLayer = "L2", Query = "callers", SourceSymbol = "UserService.GetDisplayName", ExpectedResults = ["OrderService.CreateOrder→UserService.GetDisplayName"], Description = "查找 GetDisplayName 的调用者" },
        new() { Id = "L2-004", Category = "caller_callee", ExpectedLayer = "L2", Query = "callees", SourceSymbol = "OrderService.CreateOrder", ExpectedResults = ["OrderService.CreateOrder→UserService.ValidateUser", "OrderService.CreateOrder→UserService.GetDisplayName"], Description = "查找 CreateOrder 的被调用者" },
        new() { Id = "L2-005", Category = "caller_callee", ExpectedLayer = "L2", Query = "callers", SourceSymbol = "UserService.ValidateUser", ExpectedResults = ["OrderService.CreateOrder→UserService.ValidateUser"], Description = "查找 ValidateUser 的调用者" },
        new() { Id = "L2-006", Category = "caller_callee", ExpectedLayer = "L2", Query = "callees", SourceSymbol = "Controller.Handle", ExpectedResults = ["Controller.Handle→UserService.GetName"], Description = "查找 Handle 的被调用者" },
        new() { Id = "L2-007", Category = "caller_callee", ExpectedLayer = "L2", Query = "callers", SourceSymbol = "UserService.GetName", ExpectedResults = ["Controller.Handle→UserService.GetName"], Description = "查找 GetName 的调用者" },
        new() { Id = "L2-008", Category = "caller_callee", ExpectedLayer = "L2", Query = "callees", SourceSymbol = "Repository.GetById", ExpectedResults = [], Description = "查找 GetById 的被调用者(无)" },
        new() { Id = "L2-009", Category = "caller_callee", ExpectedLayer = "L2", Query = "callers", SourceSymbol = "Repository.Save", ExpectedResults = [], Description = "查找 Save 的调用者(无)" },
        new() { Id = "L2-010", Category = "caller_callee", ExpectedLayer = "L2", Query = "callees", SourceSymbol = "Calculator.Sum", ExpectedResults = ["Calculator.Sum→Helper.Square"], Description = "查找 Sum 的被调用者" }
    ];

    private static List<TestCase> CallChainCases() =>
    [
        new() { Id = "L2-011", Category = "call_chain", ExpectedLayer = "L2", Query = "chain", SourceSymbol = "OrderService.CreateOrder", TargetSymbol = "UserService.ValidateUser", ExpectedResults = ["OrderService.CreateOrder→UserService.ValidateUser"], Description = "追踪 CreateOrder 到 ValidateUser 的调用链" },
        new() { Id = "L2-012", Category = "call_chain", ExpectedLayer = "L2", Query = "chain", SourceSymbol = "OrderService.CreateOrder", TargetSymbol = "UserService.GetDisplayName", ExpectedResults = ["OrderService.CreateOrder→UserService.GetDisplayName"], Description = "追踪 CreateOrder 到 GetDisplayName 的调用链" },
        new() { Id = "L2-013", Category = "call_chain", ExpectedLayer = "L2", Query = "chain", SourceSymbol = "Controller.Handle", TargetSymbol = "UserService.GetName", ExpectedResults = ["Controller.Handle→UserService.GetName"], Description = "追踪 Handle 到 GetName 的调用链" },
        new() { Id = "L2-014", Category = "call_chain", ExpectedLayer = "L2", Query = "chain", SourceSymbol = "Calculator.Compute", TargetSymbol = "Helper.Square", ExpectedResults = ["Calculator.Compute→Helper.Square"], Description = "追踪 Compute 到 Square 的调用链" },
        new() { Id = "L2-015", Category = "call_chain", ExpectedLayer = "L2", Query = "chain", SourceSymbol = "Calculator.Sum", TargetSymbol = "Helper.Square", ExpectedResults = ["Calculator.Sum→Helper.Square"], Description = "追踪 Sum 到 Square 的调用链" },
        new() { Id = "L2-016", Category = "call_chain", ExpectedLayer = "L2", Query = "chain", SourceSymbol = "OrderService.CreateOrder", TargetSymbol = "Helper.Square", ExpectedResults = [], Description = "追踪不相关符号间的调用链(无)" },
        new() { Id = "L2-017", Category = "call_chain", ExpectedLayer = "L2", Query = "chain", SourceSymbol = "Controller.Handle", TargetSymbol = "Helper.Square", ExpectedResults = [], Description = "追踪不相关符号间的调用链(无)" },
        new() { Id = "L2-018", Category = "call_chain", ExpectedLayer = "L2", Query = "chain", SourceSymbol = "Calculator.Compute", TargetSymbol = "Repository.GetById", ExpectedResults = [], Description = "追踪不相关符号间的调用链(无)" }
    ];

    private static List<TestCase> ImpactScopeCases() =>
    [
        new() { Id = "L2-019", Category = "impact_scope", ExpectedLayer = "L2", Query = "impact", SourceSymbol = "UserService.ValidateUser", ExpectedResults = ["OrderService.cs"], Description = "ValidateUser 变更的影响范围" },
        new() { Id = "L2-020", Category = "impact_scope", ExpectedLayer = "L2", Query = "impact", SourceSymbol = "UserService.GetDisplayName", ExpectedResults = ["OrderService.cs"], Description = "GetDisplayName 变更的影响范围" },
        new() { Id = "L2-021", Category = "impact_scope", ExpectedLayer = "L2", Query = "impact", SourceSymbol = "Helper.Square", ExpectedResults = ["Calculator.cs", "Helper.cs"], Description = "Square 变更的影响范围" },
        new() { Id = "L2-022", Category = "impact_scope", ExpectedLayer = "L2", Query = "impact", SourceSymbol = "UserService.GetName", ExpectedResults = ["Controller.cs", "UserService.cs"], Description = "GetName 变更的影响范围" },
        new() { Id = "L2-023", Category = "impact_scope", ExpectedLayer = "L2", Query = "impact", SourceSymbol = "Repository.GetById", ExpectedResults = ["Repository.cs"], Description = "GetById 变更的影响范围" },
        new() { Id = "L2-024", Category = "impact_scope", ExpectedLayer = "L2", Query = "impact", SourceSymbol = "OrderService.CreateOrder", ExpectedResults = ["OrderService.cs"], Description = "CreateOrder 变更的影响范围" },
        new() { Id = "L2-025", Category = "impact_scope", ExpectedLayer = "L2", Query = "impact", SourceSymbol = "Controller.Handle", ExpectedResults = ["Controller.cs"], Description = "Handle 变更的影响范围" }
    ];

    private static List<TestCase> InheritanceCases() =>
    [
        new() { Id = "L2-026", Category = "inheritance", ExpectedLayer = "L2", Query = "inheritors", SourceSymbol = "IRepository", ExpectedResults = ["Repository"], Description = "查找 IRepository 的实现类" },
        new() { Id = "L2-027", Category = "inheritance", ExpectedLayer = "L2", Query = "dependencies", SourceSymbol = "OrderService", ExpectedResults = ["UserService"], Description = "查找 OrderService 的依赖" },
        new() { Id = "L2-028", Category = "inheritance", ExpectedLayer = "L2", Query = "dependencies", SourceSymbol = "Controller", ExpectedResults = ["UserService"], Description = "查找 Controller 的依赖" },
        new() { Id = "L2-029", Category = "inheritance", ExpectedLayer = "L2", Query = "dependencies", SourceSymbol = "Calculator", ExpectedResults = ["Helper"], Description = "查找 Calculator 的依赖" },
        new() { Id = "L2-030", Category = "inheritance", ExpectedLayer = "L2", Query = "affected_files", SourceSymbol = "UserService", ExpectedResults = ["UserService.cs", "OrderService.cs", "Controller.cs"], Description = "UserService 变更影响的所有文件" }
    ];
}
