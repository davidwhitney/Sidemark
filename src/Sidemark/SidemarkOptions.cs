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

    /// Returns a copy with selected fields replaced. Use this rather than hand-copying fields —
    /// this object grows over time, and missed-field bugs (e.g. SourceFilePath) are easy to
    /// introduce when reconstructing it.
    public SidemarkOptions With(
        string? activitySourceExpression = null,
        DirectivePatterns? patterns = null,
        bool? disabled = null,
        string? sourceFilePath = null)
        => new()
        {
            ActivitySourceExpression = activitySourceExpression ?? ActivitySourceExpression,
            Patterns = patterns ?? Patterns,
            Disabled = disabled ?? Disabled,
            SourceFilePath = sourceFilePath ?? SourceFilePath
        };
}
