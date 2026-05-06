using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Sidemark;

namespace Sidemark.Tests;

public abstract class RewriterTestBase
{
    protected static string Rewrite(string source, SidemarkOptions? options = null)
        => SidemarkRewriter.Rewrite(source, options);

    protected static void AssertCSharpEquivalent(string expected, string actual)
    {
        var normExpected = Normalize(expected);
        var normActual = Normalize(actual);
        Assert.Equal(normExpected, normActual);
    }

    protected static string Normalize(string source)
    {
        var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(source).GetRoot();
        var stripped = (CompilationUnitSyntax)new DirectiveCommentStripper().Visit(root)!;
        return stripped.NormalizeWhitespace().ToFullString().Trim();
    }

    private sealed class DirectiveCommentStripper : Microsoft.CodeAnalysis.CSharp.CSharpSyntaxRewriter
    {
        public override SyntaxTrivia VisitTrivia(SyntaxTrivia trivia)
        {
            if (trivia.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SingleLineCommentTrivia))
            {
                var t = trivia.ToString();
                if (t.Length >= 3 && t[0] == '/' && t[1] == '/' && (t[2] == '!' || t[2] == '?'))
                {
                    return Microsoft.CodeAnalysis.CSharp.SyntaxFactory.Whitespace("");
                }
            }
            return base.VisitTrivia(trivia);
        }
    }
}
