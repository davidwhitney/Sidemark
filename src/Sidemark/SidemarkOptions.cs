namespace Sidemark;

public sealed class SidemarkOptions
{
    public string ActivitySourceExpression { get; set; } = "ActivitySource";
    public DirectivePatterns Patterns { get; set; } = new();
    public bool Disabled { get; set; }
    public static SidemarkOptions Default => new();
}
