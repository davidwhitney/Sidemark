using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sidemark.Internal;

internal sealed class SidemarkSyntaxRewriter(SidemarkOptions options) : CSharpSyntaxRewriter
{
    private DirectivePatterns Patterns => options.Patterns;

    private bool EmitLineDirectives => !string.IsNullOrEmpty(options.SourceFilePath);

    private SyntaxTriviaList HiddenLeading(SyntaxTriviaList indent)
    {
        if (!EmitLineDirectives) return indent;
        // Drop any newlines from the indent: the directive provides its own line break, and we
        // don't want a blank line between #line hidden and the synthetic statement that follows.
        var whitespaceOnly = SyntaxFactory.TriviaList(indent.Where(t => t.IsKind(SyntaxKind.WhitespaceTrivia)));
        return SyntaxFactory.ParseLeadingTrivia("#line hidden\n").AddRange(whitespaceOnly);
    }

    private StatementSyntax WrapOriginal(StatementSyntax stmt)
    {
        if (!EmitLineDirectives) return stmt;
        var line = stmt.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        var path = options.SourceFilePath!.Replace("\\", "/").Replace("\"", "\\\"");
        var directive = SyntaxFactory.ParseLeadingTrivia($"#line {line} \"{path}\"\n");

        // Insert the directive AFTER any leading newlines/blank-line trivia, so the line that
        // immediately follows the directive is the statement itself (mapped to `line`), not a
        // blank line that would consume `line` and shift the statement to `line + 1`.
        var existing = stmt.GetLeadingTrivia();
        var lastEol = -1;
        for (var i = existing.Count - 1; i >= 0; i--)
        {
            if (existing[i].IsKind(SyntaxKind.EndOfLineTrivia))
            {
                lastEol = i;
                break;
            }
        }

        SyntaxTriviaList newLeading;
        if (lastEol >= 0)
        {
            var before = SyntaxFactory.TriviaList(existing.Take(lastEol + 1));
            var after = SyntaxFactory.TriviaList(existing.Skip(lastEol + 1));
            newLeading = before.AddRange(directive).AddRange(after);
        }
        else
        {
            newLeading = directive.AddRange(existing);
        }

        return stmt.WithLeadingTrivia(newLeading);
    }

    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        if (node.Body is null)
        {
            return base.VisitMethodDeclaration(node);
        }

        var (activityPayload, eventPayload) = FindSignatureDirectives(node.ParameterList?.CloseParenToken, node.Body.OpenBraceToken);
        var hasBodyDirectives = HasAnyDirectiveInBody(node.Body);

        if (activityPayload is null && eventPayload is null && !hasBodyDirectives)
        {
            return base.VisitMethodDeclaration(node);
        }

        var activityName = !string.IsNullOrEmpty(activityPayload) ? activityPayload! : node.Identifier.ValueText;
        var entryEventName = eventPayload is null ? null
            : !string.IsNullOrEmpty(eventPayload) ? eventPayload : node.Identifier.ValueText;
        
        var sourceExpression = ResolveActivitySource(node);

        var newBody = ExpandBody(
            node.Body, activityName, sourceExpression,
            createActivity: activityPayload != null,
            entryEventName: entryEventName);
        
        return node.WithBody(newBody);
    }

    public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
    {
        if (node.Body is null)
        {
            return base.VisitLocalFunctionStatement(node);
        }

        var (activityPayload, eventPayload) = FindSignatureDirectives(node.ParameterList?.CloseParenToken, node.Body.OpenBraceToken);
        var hasBodyDirectives = HasAnyDirectiveInBody(node.Body);

        if (activityPayload is null && eventPayload is null && !hasBodyDirectives)
        {
            return base.VisitLocalFunctionStatement(node);
        }

        var activityName = !string.IsNullOrEmpty(activityPayload) ? activityPayload! : node.Identifier.ValueText;
        var entryEventName = eventPayload is null ? null
            : !string.IsNullOrEmpty(eventPayload) ? eventPayload : node.Identifier.ValueText;
        
        var sourceExpression = ResolveActivitySource(node);

        var newBody = ExpandBody(
            node.Body, activityName, sourceExpression,
            createActivity: activityPayload != null,
            entryEventName: entryEventName);
        
        return node.WithBody(newBody);
    }

    private (string? activityPayload, string? eventPayload) FindSignatureDirectives(
        SyntaxToken? closeParen, SyntaxToken? openBrace)
    {
        string? activity = null;
        string? evt = null;

        void Scan(SyntaxTriviaList list)
        {
            foreach (var t in list)
            {
                var compound = DirectiveMatcher.MatchActivityEvent(t, Patterns);
                if (compound != null)
                {
                    if (activity is null) activity = string.Empty;
                    if (evt is null) evt = compound;
                    continue;
                }

                if (activity is null)
                {
                    var a = DirectiveMatcher.MatchActivity(t, Patterns);
                    if (a != null) activity = a;
                }
                if (evt is null)
                {
                    var e = DirectiveMatcher.MatchEvent(t, Patterns);
                    if (e != null) evt = e;
                }
            }
        }

        if (closeParen is { } cp) Scan(cp.TrailingTrivia);
        if (openBrace is { } ob) Scan(ob.LeadingTrivia);
        return (activity, evt);
    }

    private bool HasAnyDirectiveInBody(BlockSyntax body)
    {
        foreach (var stmt in body.DescendantNodes(n => n is not LocalFunctionStatementSyntax).OfType<StatementSyntax>())
        {
            foreach (var t in stmt.GetLeadingTrivia())
            {
                if (DirectiveMatcher.MatchesAnyRole(t, Patterns)) return true;
            }
            foreach (var t in stmt.GetTrailingTrivia())
            {
                if (DirectiveMatcher.MatchesAnyRole(t, Patterns)) return true;
            }
        }
        foreach (var catchClause in body.DescendantNodes(n => n is not LocalFunctionStatementSyntax).OfType<CatchClauseSyntax>())
        {
            if (FindCatchAnnotation(catchClause) != null) return true;
        }
        return false;
    }

    private SyntaxTrivia? FindCatchAnnotation(CatchClauseSyntax catchClause)
    {
        if (catchClause.Declaration is { CloseParenToken: var cp })
        {
            foreach (var t in cp.TrailingTrivia)
            {
                if (DirectiveMatcher.MatchTag(t, Patterns) != null) return t;
            }
        }
        if (catchClause.Block?.OpenBraceToken is { } ob)
        {
            foreach (var t in ob.LeadingTrivia)
            {
                if (DirectiveMatcher.MatchTag(t, Patterns) != null) return t;
            }
        }
        return null;
    }

    private string ResolveActivitySource(SyntaxNode node) =>
        ActivitySourceResolver.Resolve(node) ?? options.ActivitySourceExpression;

    private BlockSyntax ExpandBody(
        BlockSyntax body, string activityName, string sourceExpression,
        bool createActivity, string? entryEventName)
    {
        var expander = new DirectiveExpander(this);
        var expanded = (BlockSyntax)expander.Visit(body)!;

        if (!createActivity && entryEventName is null)
        {
            return expanded;
        }

        var indentTemplate = expanded.Statements.Count > 0
            ? IndentOnly(expanded.Statements[0].GetLeadingTrivia())
            : SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine("\n"));

        var prepended = new List<StatementSyntax>();
        if (createActivity)
        {
            prepended.Add(BuildScopeStatement(activityName, sourceExpression).WithLeadingTrivia(HiddenLeading(indentTemplate)));
        }
        if (entryEventName is { } evt)
        {
            prepended.Add(BuildAddEvent(evt).WithLeadingTrivia(HiddenLeading(indentTemplate)));
        }
        prepended.AddRange(expanded.Statements);
        return expanded.WithStatements(SyntaxFactory.List(prepended));
    }

    private static SyntaxTriviaList IndentOnly(SyntaxTriviaList trivia)
    {
        var result = new List<SyntaxTrivia>();
        var pendingNewline = false;
        var pendingWhitespace = new List<SyntaxTrivia>();

        foreach (var t in trivia)
        {
            if (t.IsKind(SyntaxKind.EndOfLineTrivia))
            {
                pendingNewline = true;
                pendingWhitespace.Clear();
            }
            else if (t.IsKind(SyntaxKind.WhitespaceTrivia))
            {
                pendingWhitespace.Add(t);
            }
            else
            {
                pendingNewline = false;
                pendingWhitespace.Clear();
            }
        }

        if (pendingNewline)
        {
            result.Add(SyntaxFactory.EndOfLine("\n"));
        }
        result.AddRange(pendingWhitespace);
        return SyntaxFactory.TriviaList(result);
    }

    private void AppendExpandedStatement(StatementSyntax stmt, List<StatementSyntax> output)
    {
        var indent = IndentOnly(stmt.GetLeadingTrivia());

        foreach (var t in stmt.GetLeadingTrivia())
        {
            var name = DirectiveMatcher.MatchEvent(t, Patterns);
            if (!string.IsNullOrEmpty(name))
            {
                output.Add(BuildAddEvent(name!).WithLeadingTrivia(HiddenLeading(indent)));
            }
        }
        foreach (var t in stmt.GetTrailingTrivia())
        {
            var name = DirectiveMatcher.MatchEvent(t, Patterns);
            if (!string.IsNullOrEmpty(name))
            {
                output.Add(BuildAddEvent(name!).WithLeadingTrivia(HiddenLeading(indent)));
            }
        }

        output.Add(WrapOriginal(stmt));

        if (stmt is LocalDeclarationStatementSyntax localDecl)
        {
            var tagPayloads = new List<string?>();
            foreach (var t in stmt.GetLeadingTrivia())
            {
                var p = DirectiveMatcher.MatchTag(t, Patterns);
                if (p != null) tagPayloads.Add(p);
            }

            foreach (var t in stmt.GetTrailingTrivia())
            {
                var p = DirectiveMatcher.MatchTag(t, Patterns);
                if (p != null) tagPayloads.Add(p);
            }

            foreach (var payload in tagPayloads)
            {
                foreach (var v in localDecl.Declaration.Variables)
                {
                    var key = string.IsNullOrEmpty(payload) ? v.Identifier.ValueText : payload!;
                    output.Add(BuildSetTag(key, v.Identifier.ValueText).WithLeadingTrivia(HiddenLeading(indent)));
                }
            }
        }
    }

    private const string ActivityCurrent = "System.Diagnostics.Activity.Current";

    private static StatementSyntax BuildScopeStatement(string name, string sourceExpression)
    {
        return SyntaxFactory.ParseStatement(
            $"using var __sidemarkScope = {sourceExpression}.StartActivity({Quote(name)});\n");
    }

    private static StatementSyntax BuildAddEvent(string name)
    {
        return SyntaxFactory.ParseStatement(
            $"{ActivityCurrent}?.AddEvent(new System.Diagnostics.ActivityEvent({Quote(name)}));\n");
    }

    private static StatementSyntax BuildSetTag(string key, string valueExpression)
    {
        return SyntaxFactory.ParseStatement(
            $"{ActivityCurrent}?.SetTag({Quote(key)}, {valueExpression});\n");
    }

    private static StatementSyntax BuildCatchSetStatus(string? exceptionVariable)
    {
        if (string.IsNullOrEmpty(exceptionVariable))
        {
            return SyntaxFactory.ParseStatement(
                $"{ActivityCurrent}?.SetStatus(System.Diagnostics.ActivityStatusCode.Error);\n");
        }
        return SyntaxFactory.ParseStatement(
            $"{ActivityCurrent}?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, {exceptionVariable}.Message);\n");
    }

    private static string Quote(string s) =>
        "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private sealed class DirectiveExpander(SidemarkSyntaxRewriter outer) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitBlock(BlockSyntax node)
        {
            var visited = (BlockSyntax)base.VisitBlock(node)!;
            var expanded = new List<StatementSyntax>();
            foreach (var stmt in visited.Statements)
            {
                outer.AppendExpandedStatement(stmt, expanded);
            }
            return visited.WithStatements(SyntaxFactory.List(expanded));
        }

        public override SyntaxNode? VisitCatchClause(CatchClauseSyntax node)
        {
            var visited = (CatchClauseSyntax)base.VisitCatchClause(node)!;
            if (outer.FindCatchAnnotation(visited) is null || visited.Block is null)
            {
                return visited;
            }

            var indent = visited.Block.Statements.Count > 0
                ? IndentOnly(visited.Block.Statements[0].GetLeadingTrivia())
                : SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine("\n"));

            var setStatus = BuildCatchSetStatus(visited.Declaration?.Identifier.ValueText)
                .WithLeadingTrivia(outer.HiddenLeading(indent));

            var newStatements = new List<StatementSyntax> { setStatus };
            newStatements.AddRange(visited.Block.Statements);
            var newBlock = visited.Block.WithStatements(SyntaxFactory.List(newStatements));
            return visited.WithBlock(newBlock);
        }

        public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node) => node;
    }
}
