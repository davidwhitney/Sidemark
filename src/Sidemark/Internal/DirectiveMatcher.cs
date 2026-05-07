using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Sidemark.Internal;

internal static class DirectiveMatcher
{
    public static string? MatchActivity(SyntaxTrivia trivia, DirectivePatterns patterns) =>
        MatchActivityEvent(trivia, patterns) != null ? null : TryMatch(trivia, patterns.ActivityPattern);

    public static string? MatchTag(SyntaxTrivia trivia, DirectivePatterns patterns) =>
        MatchActivityEvent(trivia, patterns) != null ? null : TryMatch(trivia, patterns.TagPattern);

    public static string? MatchEvent(SyntaxTrivia trivia, DirectivePatterns patterns) =>
        MatchActivityEvent(trivia, patterns) != null ? null : TryMatch(trivia, patterns.EventPattern);

    public static string? MatchActivityEvent(SyntaxTrivia trivia, DirectivePatterns patterns)
        => TryMatch(trivia, patterns.ActivityEventPattern);

    public static bool MatchesAnyRole(SyntaxTrivia trivia, DirectivePatterns patterns)
        => MatchActivity(trivia, patterns) != null
            || MatchTag(trivia, patterns) != null
            || MatchEvent(trivia, patterns) != null
            || MatchActivityEvent(trivia, patterns) != null;

    private static string? TryMatch(SyntaxTrivia trivia, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return null;
        if (!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)) return null;

        var text = trivia.ToString();
        
        if (text.Length < pattern.Length) return null;
        if (!text.StartsWith(pattern, StringComparison.Ordinal)) return null;

        return text.Length > pattern.Length ? text.Substring(pattern.Length).Trim() : string.Empty;
    }
}
