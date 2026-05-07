namespace Sidemark;

public sealed class SidemarkOptions
{
    public string ActivitySourceExpression { get; set; } = "ActivitySource";
    public DirectivePatterns Patterns { get; set; } = new();
    public bool Disabled { get; set; }

    /// Absolute path of the original source file. When set, the rewriter emits #line directives so
    /// debuggers map the rewritten obj-folder file back to this path for breakpoints/stepping.
    public string? SourceFilePath { get; set; }

    public static SidemarkOptions Default => new();
}
