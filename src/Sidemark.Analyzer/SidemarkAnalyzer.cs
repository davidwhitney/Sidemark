using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Sidemark.Analyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SidemarkAnalyzer : DiagnosticAnalyzer
{
    private const string ActivityPattern = "//?";
    private const string TagPattern = "//?";
    private const string EventPattern = "//!";
    private const string CompoundPattern = "//?!";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            Diagnostics.TagOnNonLocalDeclaration,
            Diagnostics.EventDirectiveMissingName,
            Diagnostics.DirectiveOnUnsupportedMember,
            Diagnostics.CompoundMarkerOffSignature,
            Diagnostics.DuplicateTagKey,
            Diagnostics.CatchAnnotationHasIgnoredPayload);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(
            AnalyzeMethodLike,
            SyntaxKind.MethodDeclaration,
            SyntaxKind.LocalFunctionStatement);

        context.RegisterSyntaxNodeAction(
            AnalyzeUnsupportedMember,
            SyntaxKind.ConstructorDeclaration,
            SyntaxKind.DestructorDeclaration,
            SyntaxKind.OperatorDeclaration,
            SyntaxKind.ConversionOperatorDeclaration,
            SyntaxKind.GetAccessorDeclaration,
            SyntaxKind.SetAccessorDeclaration,
            SyntaxKind.AddAccessorDeclaration,
            SyntaxKind.RemoveAccessorDeclaration,
            SyntaxKind.InitAccessorDeclaration);
    }

    private static void AnalyzeMethodLike(SyntaxNodeAnalysisContext context)
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
            // Expression-bodied or abstract: signature directive cannot be applied.
            ReportSignatureDirective(context, closeParen, openBrace: null, memberKind);
            return;
        }

        AnalyzeBody(context, body);
    }

    private static void AnalyzeUnsupportedMember(SyntaxNodeAnalysisContext context)
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
                ReportAccessorSignatureDirective(context, a);
                return;
            default:
                return;
        }

        ReportSignatureDirective(context, closeParen, openBrace, memberKind);
    }

    private static void ReportAccessorSignatureDirective(
        SyntaxNodeAnalysisContext context,
        AccessorDeclarationSyntax accessor)
    {
        var checkTokens = new[] { accessor.Keyword, accessor.Body?.OpenBraceToken ?? default };
        foreach (var token in checkTokens)
        {
            if (token == default) continue;
            foreach (var t in token.TrailingTrivia)
            {
                if (IsAnyDirectiveStart(t))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.DirectiveOnUnsupportedMember, t.GetLocation(), "accessor"));
                    return;
                }
            }
            foreach (var t in token.LeadingTrivia)
            {
                if (IsAnyDirectiveStart(t))
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
        string memberKind)
    {
        if (closeParen is { } cp)
        {
            foreach (var t in cp.TrailingTrivia)
            {
                if (IsAnyDirectiveStart(t))
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
                if (IsAnyDirectiveStart(t))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.DirectiveOnUnsupportedMember, t.GetLocation(), memberKind));
                    return;
                }
            }
        }
    }

    private static void AnalyzeBody(SyntaxNodeAnalysisContext context, BlockSyntax body)
    {
        var tagKeySites = new Dictionary<string, List<Location>>();

        foreach (var stmt in body.DescendantNodes(_ => true).OfType<StatementSyntax>())
        {
            if (stmt is LocalFunctionStatementSyntax) continue;
            CheckStatementTrivia(context, stmt, stmt.GetLeadingTrivia(), tagKeySites);
            CheckStatementTrivia(context, stmt, stmt.GetTrailingTrivia(), tagKeySites);
        }

        foreach (var catchClause in body.DescendantNodes(_ => true).OfType<CatchClauseSyntax>())
        {
            CheckCatchClause(context, catchClause);
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
        Dictionary<string, List<Location>> tagKeySites)
    {
        foreach (var t in trivia)
        {
            if (!t.IsKind(SyntaxKind.SingleLineCommentTrivia)) continue;
            var text = t.ToString();
            if (text.Length < 3 || text[0] != '/' || text[1] != '/') continue;

            // Compound //?! has higher precedence than //? or //!.
            if (text.StartsWith(CompoundPattern))
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.CompoundMarkerOffSignature, t.GetLocation()));
                continue;
            }

            var marker = text[2];
            var payload = text.Length > 3 ? text.Substring(3).Trim() : string.Empty;

            if (marker == '?')
            {
                if (stmt is LocalDeclarationStatementSyntax localDecl)
                {
                    foreach (var v in localDecl.Declaration.Variables)
                    {
                        var key = string.IsNullOrEmpty(payload) ? v.Identifier.ValueText : payload;
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
            }
            else if (marker == '!' && string.IsNullOrEmpty(payload))
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.EventDirectiveMissingName, t.GetLocation()));
            }
        }
    }

    private static void CheckCatchClause(SyntaxNodeAnalysisContext context, CatchClauseSyntax catchClause)
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
            if (!t.IsKind(SyntaxKind.SingleLineCommentTrivia)) continue;
            var text = t.ToString();
            if (text.Length < 3 || !text.StartsWith(TagPattern)) continue;
            if (text.StartsWith(CompoundPattern)) continue; // out of scope here

            var payload = text.Length > 3 ? text.Substring(3).Trim() : string.Empty;
            if (!string.IsNullOrEmpty(payload))
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.CatchAnnotationHasIgnoredPayload, t.GetLocation()));
            }
        }
    }

    private static bool IsAnyDirectiveStart(SyntaxTrivia trivia)
    {
        if (!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)) return false;
        var text = trivia.ToString();
        return text.Length >= 3 && text[0] == '/' && text[1] == '/' && (text[2] == '?' || text[2] == '!');
    }
}
