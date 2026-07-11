## Release Tracking

### 1.0.0

New Diagnostics:

- DPSK1001: AOT incompatible: Dictionary<string, object?> is unsafe under NativeAOT.
- DPSK1002: AOT incompatible: Dictionary<string, object> is unsafe under NativeAOT.
- DPSK1003: AOT incompatible: Type inherits from Dictionary<string, object?>.
- DPSK1004: AOT incompatible: dynamic keyword is not supported under NativeAOT.
- DPSK1005: Code style: using directive in .cs file should be in GlobalUsings.cs.
- DPSK1006: Code style: method with more than 3 parameters should use an Options class.
- DPSK2001: Console.ReadLine() must be wrapped with IsInputRedirected check.
- DPSK2002: Console.ReadKey() must be wrapped with IsInputRedirected check.
- DPSK2003: Console.Read() must be wrapped with IsInputRedirected check.
- DPSK3001: Fire-and-forget async call without CancellationToken.
- DPSK3002: Task.Run without CancellationToken.
- DPSK3003: WaitForExitAsync followed by ReadToEndAsync causes deadlock.
- DPSK3004: RedirectStandardError=true but stderr is never consumed.
- DPSK3005: async void method exception cannot be caught; use async Task instead.
- DPSK3006: .Result/.Wait() blocking call may cause deadlock; use await instead.
- DPSK3007: Sequential await in loop should use Task.WhenAll for concurrent execution.
- DPSK3008: Library code await must use ConfigureAwait(false).
- DPSK3009: Test code await must use ConfigureAwait(true).
- DPSK4001: lock statement in async method may cause deadlock; use SemaphoreSlim.
- DPSK4002: SemaphoreSlim.Wait() without timeout may hang indefinitely.
- DPSK5001: Thread.Sleep blocks the thread; use Task.Delay instead.
- DPSK6001: Nested loop on same collection is O(n^2); consider pre-indexing.
- DPSK6002: O(n) operation (Contains/IndexOf) inside loop; use HashSet for O(1) lookup.
- DPSK6003: Method-level List.Contains frequency suggests HashSet conversion.
- DPSK6004: Linear search on static readonly collection; use Array.BinarySearch.
- DPSK6005: List.Insert(0, item) is O(n); use Add + Reverse.
- DPSK6006: Range query in loop can use binary search for O(log n).
- DPSK6007: String operation can be optimized with Span<char> to reduce allocation.
- DPSK6008: foreach loop can be replaced with LINQ chain expression.
- DPSK6009: Code style: Parallel.ForEach is prohibited; use LINQ or Task.WhenAll instead.
- DPSK7001: Dead code: private/internal method is never referenced.
- DPSK7002: Dead code: internal type is never referenced.
- DPSK7003: Dead code: enum type is never referenced.
- DPSK1010: Code style: switch on string should use enum with [EnumValue] attributes.
- DPSK6010: Code style: LINQ chain with more than 8 calls should be split into named functions.
- DPSK10001: Project structure: folder with more than 20 directly exposed files should be split.
- DPSK10002: Project structure: folder should not mix files and subfolders (pure files or pure folders).
- DPSK10005: Code style: string switch expression with 3+ arms should use enum with [EnumValue].
- DPSK10006: Code style: Dictionary<string, string> with Key==Value is redundant; use enum + [EnumValue].
- DPSK10007: Code style: string literals matching [EnumValue] values should use XxxExtensions.ToValue().
