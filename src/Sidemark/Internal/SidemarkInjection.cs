namespace Sidemark.Internal;

internal static class SidemarkInjection
{
    /// Local variable the rewriter introduces at the top of every instrumented method body
    /// (the `using var __sidemarkScope = ...StartActivity(...)` line). User code declaring a
    /// local/parameter with this name in such a method would collide.
    public const string ScopeVariableName = "__sidemarkScope";

    /// Escape an absolute file path for embedding in a `#line N "<path>"` directive: forward slashes
    /// (legal on every platform; avoids C# string-literal backslash escaping), and any literal
    /// double-quotes are escaped.
    public static string EscapePathForLineDirective(string path) =>
        path.Replace("\\", "/").Replace("\"", "\\\"");
}
