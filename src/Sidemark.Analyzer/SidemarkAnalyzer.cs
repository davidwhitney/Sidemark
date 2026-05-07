using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Sidemark.Internal;

namespace Sidemark.Analyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SidemarkAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        Diagnostics.TagOnNonLocalDeclaration,
        Diagnostics.EventDirectiveMissingName,
        Diagnostics.DirectiveOnUnsupportedMember,
        Diagnostics.CompoundMarkerOffSignature,
        Diagnostics.DuplicateTagKey,
        Diagnostics.CatchAnnotationHasIgnoredPayload
    ];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            var patterns = ResolvePatterns(start.Compilation, start.CancellationToken);

            start.RegisterSyntaxNodeAction(
                ctx => AnalyzeMethodLike(ctx, patterns),
                SyntaxKind.MethodDeclaration,
                SyntaxKind.LocalFunctionStatement);

            start.RegisterSyntaxNodeAction(
                ctx => AnalyzeUnsupportedMember(ctx, patterns),
                SyntaxKind.ConstructorDeclaration,
                SyntaxKind.DestructorDeclaration,
                SyntaxKind.OperatorDeclaration,
                SyntaxKind.ConversionOperatorDeclaration,
                SyntaxKind.GetAccessorDeclaration,
                SyntaxKind.SetAccessorDeclaration,
                SyntaxKind.AddAccessorDeclaration,
                SyntaxKind.RemoveAccessorDeclaration,
                SyntaxKind.InitAccessorDeclaration);
        });
    }

    private static DirectivePatterns ResolvePatterns(Compilation compilation, System.Threading.CancellationToken cancellationToken)
    {
        var roots = compilation.SyntaxTrees
            .Select(t => t.GetRoot(cancellationToken))
            .ToList();
        return ConfigurationResolver.TryResolve(roots)?.Patterns ?? new DirectivePatterns();
    }

    private static void AnalyzeMethodLike(SyntaxNodeAnalysisContext context, DirectivePatterns patterns)
    {
        SyntaxToken? closeParen;
        BlockSyntax? body;
        string memberKind;

        switch (context.Node)
        {
            case MethodDeclarationSyntax m:
                closeParen = m.ParameterList?.CloseParenToken;
                body = m.Body;
                memberKind = "method";
                break;
            case LocalFunctionStatementSyntax fn:
                closeParen = fn.ParameterList?.CloseParenToken;
                body = fn.Body;
                memberKind = "local function";
                break;
            default:
                return;
        }

        if (body is null)
        {
            ReportSignatureDirective(context, closeParen, openBrace: null, memberKind, patterns);
            return;
        }

        AnalyzeBody(context, body, patterns);
    }

    private static void AnalyzeUnsupportedMember(SyntaxNodeAnalysisContext context, DirectivePatterns patterns)
    {
        SyntaxToken? closeParen;
        SyntaxToken? openBrace;
        string memberKind;

        switch (context.Node)
        {
            case ConstructorDeclarationSyntax c:
                closeParen = c.ParameterList?.CloseParenToken;
                openBrace = c.Body?.OpenBraceToken;
                memberKind = "constructor";
                break;
            case DestructorDeclarationSyntax d:
                closeParen = d.ParameterList?.CloseParenToken;
                openBrace = d.Body?.OpenBraceToken;
                memberKind = "destructor";
                break;
            case OperatorDeclarationSyntax op:
                closeParen = op.ParameterList?.CloseParenToken;
                openBrace = op.Body?.OpenBraceToken;
                memberKind = "operator";
                break;
            case ConversionOperatorDeclarationSyntax co:
                closeParen = co.ParameterList?.CloseParenToken;
                openBrace = co.Body?.OpenBraceToken;
                memberKind = "conversion operator";
                break;
            case AccessorDeclarationSyntax a:
                ReportAccessorSignatureDirective(context, a, patterns);
                return;
            default:
                return;
        }

        ReportSignatureDirective(context, closeParen, openBrace, memberKind, patterns);
    }

    private static void ReportAccessorSignatureDirective(
        SyntaxNodeAnalysisContext context,
        AccessorDeclarationSyntax accessor,
        DirectivePatterns patterns)
    {
        var checkTokens = new[] { accessor.Keyword, accessor.Body?.OpenBraceToken ?? default };
        foreach (var token in checkTokens)
        {
            if (token == default) continue;
            foreach (var t in token.TrailingTrivia)
            {
                if (DirectiveMatcher.MatchesAnyRole(t, patterns))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.DirectiveOnUnsupportedMember, t.GetLocation(), "accessor"));
                    return;
                }
            }
            foreach (var t in token.LeadingTrivia)
            {
                if (DirectiveMatcher.MatchesAnyRole(t, patterns))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.DirectiveOnUnsupportedMember, t.GetLocation(), "accessor"));
                    return;
                }
            }
        }
    }

    private static void ReportSignatureDirective(
        SyntaxNodeAnalysisContext context,
        SyntaxToken? closeParen,
        SyntaxToken? openBrace,
        string memberKind,
        DirectivePatterns patterns)
    {
        if (closeParen is { } cp)
        {
            foreach (var t in cp.TrailingTrivia)
            {
                if (DirectiveMatcher.MatchesAnyRole(t, patterns))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.DirectiveOnUnsupportedMember, t.GetLocation(), memberKind));
                    return;
                }
            }
        }
        if (openBrace is { } ob)
        {
            foreach (var t in ob.LeadingTrivia)
            {
                if (DirectiveMatcher.MatchesAnyRole(t, patterns))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.DirectiveOnUnsupportedMember, t.GetLocation(), memberKind));
                    return;
                }
            }
        }
    }

    private static void AnalyzeBody(SyntaxNodeAnalysisContext context, BlockSyntax body, DirectivePatterns patterns)
    {
        var tagKeySites = new Dictionary<string, List<Location>>();

        foreach (var stmt in body.DescendantNodes(_ => true).OfType<StatementSyntax>())
        {
            if (stmt is LocalFunctionStatementSyntax) continue;
            CheckStatementTrivia(context, stmt, stmt.GetLeadingTrivia(), tagKeySites, patterns);
            CheckStatementTrivia(context, stmt, stmt.GetTrailingTrivia(), tagKeySites, patterns);
        }

        foreach (var catchClause in body.DescendantNodes(_ => true).OfType<CatchClauseSyntax>())
        {
            CheckCatchClause(context, catchClause, patterns);
        }

        foreach (var entry in tagKeySites)
        {
            if (entry.Value.Count > 1)
            {
                foreach (var loc in entry.Value)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.DuplicateTagKey, loc, entry.Key));
                }
            }
        }
    }

    private static void CheckStatementTrivia(
        SyntaxNodeAnalysisContext context,
        StatementSyntax stmt,
        SyntaxTriviaList trivia,
        Dictionary<string, List<Location>> tagKeySites,
        DirectivePatterns patterns)
    {
        foreach (var t in trivia)
        {
            // Compound (e.g. //?!) is only valid on a method/local-function signature.
            if (DirectiveMatcher.MatchActivityEvent(t, patterns) != null)
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.CompoundMarkerOffSignature, t.GetLocation()));
                continue;
            }

            var tagPayload = DirectiveMatcher.MatchTag(t, patterns);
            if (tagPayload != null)
            {
                if (stmt is LocalDeclarationStatementSyntax localDecl)
                {
                    foreach (var v in localDecl.Declaration.Variables)
                    {
                        var key = string.IsNullOrEmpty(tagPayload) ? v.Identifier.ValueText : tagPayload;
                        if (!tagKeySites.TryGetValue(key, out var sites))
                        {
                            sites = new List<Location>();
                            tagKeySites[key] = sites;
                        }
                        sites.Add(t.GetLocation());
                    }
                }
                else
                {
                    context.ReportDiagnostic(Diagnostic.Create(Diagnostics.TagOnNonLocalDeclaration, t.GetLocation()));
                }
                continue;
            }

            var eventPayload = DirectiveMatcher.MatchEvent(t, patterns);
            if (eventPayload != null && string.IsNullOrEmpty(eventPayload))
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.EventDirectiveMissingName, t.GetLocation()));
            }
        }
    }

    private static void CheckCatchClause(SyntaxNodeAnalysisContext context, CatchClauseSyntax catchClause, DirectivePatterns patterns)
    {
        var triviaSpots = new List<SyntaxTrivia>();
        if (catchClause.Declaration is { CloseParenToken: var cp })
        {
            triviaSpots.AddRange(cp.TrailingTrivia);
        }
        if (catchClause.Block?.OpenBraceToken is { } ob)
        {
            triviaSpots.AddRange(ob.LeadingTrivia);
        }

        foreach (var t in triviaSpots)
        {
            var payload = DirectiveMatcher.MatchTag(t, patterns);
            if (!string.IsNullOrEmpty(payload))
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.CatchAnnotationHasIgnoredPayload, t.GetLocation()));
            }
        }
    }
}
