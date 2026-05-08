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
        Diagnostics.CatchAnnotationHasIgnoredPayload,
        Diagnostics.ReservedScopeVariableName
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
        ParameterListSyntax? parameterList;
        string memberKind;

        switch (context.Node)
        {
            case MethodDeclarationSyntax m:
                closeParen = m.ParameterList?.CloseParenToken;
                body = m.Body;
                parameterList = m.ParameterList;
                memberKind = "method";
                break;
            case LocalFunctionStatementSyntax fn:
                closeParen = fn.ParameterList?.CloseParenToken;
                body = fn.Body;
                parameterList = fn.ParameterList;
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

        if (SignatureHasActivityInjection(closeParen, body.OpenBraceToken, patterns))
        {
            CheckReservedScopeVariableName(context, parameterList, body);
        }
    }

    private static bool SignatureHasActivityInjection(SyntaxToken? closeParen, SyntaxToken openBrace, DirectivePatterns patterns)
    {
        bool ScanForActivity(SyntaxTriviaList list)
        {
            foreach (var t in list)
            {
                if (DirectiveMatcher.MatchActivityEvent(t, patterns) != null) return true;
                if (DirectiveMatcher.MatchActivity(t, patterns) != null) return true;
            }
            return false;
        }

        if (closeParen is { } cp && ScanForActivity(cp.TrailingTrivia)) return true;
        return ScanForActivity(openBrace.LeadingTrivia);
    }

    private static void CheckReservedScopeVariableName(
        SyntaxNodeAnalysisContext context,
        ParameterListSyntax? parameters,
        BlockSyntax body)
    {
        const string reserved = SidemarkInjection.ScopeVariableName;

        if (parameters != null)
        {
            foreach (var p in parameters.Parameters)
            {
                if (p.Identifier.ValueText == reserved)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.ReservedScopeVariableName, p.Identifier.GetLocation(), reserved));
                }
            }
        }

        // Don't descend into nested local functions: they have their own activity scope (or none),
        // so a local named __sidemarkScope inside one doesn't collide with this method's injection.
        foreach (var node in body.DescendantNodes(n => n is not LocalFunctionStatementSyntax))
        {
            switch (node)
            {
                case VariableDeclaratorSyntax v when v.Identifier.ValueText == reserved:
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.ReservedScopeVariableName, v.Identifier.GetLocation(), reserved));
                    break;
                case SingleVariableDesignationSyntax svd when svd.Identifier.ValueText == reserved:
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.ReservedScopeVariableName, svd.Identifier.GetLocation(), reserved));
                    break;
                case ForEachStatementSyntax fe when fe.Identifier.ValueText == reserved:
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.ReservedScopeVariableName, fe.Identifier.GetLocation(), reserved));
                    break;
                case CatchDeclarationSyntax cd when cd.Identifier.ValueText == reserved:
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.ReservedScopeVariableName, cd.Identifier.GetLocation(), reserved));
                    break;
            }
        }
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
        // For accessors the directive can land on the `get`/`set`/etc. keyword's trailing trivia, or
        // on the open-brace's leading trivia.
        ReportFirstSignatureDirective(context, "accessor", patterns,
            accessor.Keyword.TrailingTrivia,
            accessor.Body?.OpenBraceToken.TrailingTrivia ?? default,
            accessor.Body?.OpenBraceToken.LeadingTrivia ?? default);
    }

    private static void ReportSignatureDirective(
        SyntaxNodeAnalysisContext context,
        SyntaxToken? closeParen,
        SyntaxToken? openBrace,
        string memberKind,
        DirectivePatterns patterns)
    {
        ReportFirstSignatureDirective(context, memberKind, patterns,
            closeParen?.TrailingTrivia ?? default,
            openBrace?.LeadingTrivia ?? default);
    }

    private static void ReportFirstSignatureDirective(
        SyntaxNodeAnalysisContext context,
        string memberKind,
        DirectivePatterns patterns,
        params SyntaxTriviaList[] triviaLists)
    {
        foreach (var list in triviaLists)
        {
            foreach (var t in list)
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

        // Don't descend into nested local functions: they get their own AnalyzeMethodLike pass and
        // their statements/catch clauses must not be attributed to the enclosing method.
        foreach (var stmt in body.DescendantNodes(n => n is not LocalFunctionStatementSyntax).OfType<StatementSyntax>())
        {
            CheckStatementTrivia(context, stmt, stmt.GetLeadingTrivia(), tagKeySites, patterns);
            CheckStatementTrivia(context, stmt, stmt.GetTrailingTrivia(), tagKeySites, patterns);
        }

        foreach (var catchClause in body.DescendantNodes(n => n is not LocalFunctionStatementSyntax).OfType<CatchClauseSyntax>())
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
