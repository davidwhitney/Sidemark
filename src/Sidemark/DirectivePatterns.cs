namespace Sidemark;

public sealed class DirectivePatterns
{
    public const string DefaultActivity = "//?";
    public const string DefaultTag = "//?";
    public const string DefaultBaggage = "//=>";
    public const string DefaultEvent = "//!";
    public const string DefaultActivityEvent = "//?!";

    public string ActivityPattern { get; set; } = DefaultActivity;
    public string TagPattern { get; set; } = DefaultTag;
    public string BaggagePattern { get; set; } = DefaultBaggage;
    public string EventPattern { get; set; } = DefaultEvent;
    public string ActivityEventPattern { get; set; } = DefaultActivityEvent;
    public static DirectivePatterns Default => new();
}
