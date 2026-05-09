using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sidemark.Internal;

internal static class ActivitySourceResolver
{
    public static string? Resolve(SyntaxNode methodOrLocalFunction)
    {
        // Walk up from the method/local-function looking for a non-assembly [SidemarkActivitySource]
        // attribute on it or any enclosing type. Assembly-level attributes are handled separately
        // below; skip them in the walk so we don't double-handle.
        foreach (var node in methodOrLocalFunction.AncestorsAndSelf())
        {
            var attrLists = GetAttributeLists(node);
            if (attrLists is null) continue;

            foreach (var attrList in attrLists)
            {
                if (attrList.IsAssemblyTarget()) continue;

                foreach (var attr in attrList.Attributes)
                {
                    var resolved = TryExtract(attr);
                    if (resolved != null) return resolved;
                }
            }
        }

        return ResolveAssemblyLevel(methodOrLocalFunction.SyntaxTree.GetRoot());
    }

    public static string? ResolveAssemblyLevel(SyntaxNode root)
    {
        foreach (var attr in root.AssemblyAttributes())
        {
            var resolved = TryExtract(attr);
            if (resolved != null) return resolved;
        }
        return null;
    }

    private static SyntaxList<AttributeListSyntax>? GetAttributeLists(SyntaxNode node) => node switch
    {
        MethodDeclarationSyntax m => m.AttributeLists,
        LocalFunctionStatementSyntax fn => fn.AttributeLists,
        ClassDeclarationSyntax c => c.AttributeLists,
        StructDeclarationSyntax s => s.AttributeLists,
        RecordDeclarationSyntax r => r.AttributeLists,
        InterfaceDeclarationSyntax i => i.AttributeLists,
        _ => null
    };

    private static string? TryExtract(AttributeSyntax attr)
    {
        if (!attr.MatchesType(nameof(SidemarkActivitySourceAttribute))) return null;

        var args = attr.ArgumentList?.Arguments;
        if (args is null || args.Value.Count < 2) return null;

        var typeOf = args.Value[0].Expression as TypeOfExpressionSyntax;
        if (typeOf is null) return null;

        var containingType = typeOf.Type.ToString();
        var memberName = ExtractMemberName(args.Value[1].Expression);
        if (string.IsNullOrEmpty(memberName)) return null;

        return $"{containingType}.{memberName}";
    }

    private static string? ExtractMemberName(ExpressionSyntax expr)
    {
        switch (expr)
        {
            case LiteralExpressionSyntax lit when lit.IsKind(SyntaxKind.StringLiteralExpression):
                return lit.Token.ValueText;

            case InvocationExpressionSyntax inv when IsNameOf(inv):
                return inv.ArgumentList.Arguments.Count != 1
                    ? null
                    : TerminalIdentifier(inv.ArgumentList.Arguments[0].Expression);

            default:
                return null;
        }
    }

    private static bool IsNameOf(InvocationExpressionSyntax inv)
    {
        return inv.Expression is IdentifierNameSyntax id && id.Identifier.Text == "nameof";
    }

    private static string? TerminalIdentifier(ExpressionSyntax expr) => expr switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
        _ => null
    };
}
