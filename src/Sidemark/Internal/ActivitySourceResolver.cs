using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sidemark.Internal;

internal static class ActivitySourceResolver
{
    public static string? Resolve(SyntaxNode methodOrLocalFunction)
    {
        SyntaxNode? node = methodOrLocalFunction;
        while (node != null)
        {
            var attrLists = GetAttributeLists(node);
            if (attrLists != null)
            {
                foreach (var attrList in attrLists)
                {
                    if (attrList.Target?.Identifier.IsKind(SyntaxKind.AssemblyKeyword) == true)
                    {
                        continue;
                    }

                    foreach (var attr in attrList.Attributes)
                    {
                        var resolved = TryExtract(attr);
                        if (resolved != null) return resolved;
                    }
                }
            }
            node = node.Parent;
        }

        var root = methodOrLocalFunction.SyntaxTree.GetRoot();
        return ResolveAssemblyLevel(root);
    }

    public static string? ResolveAssemblyLevel(SyntaxNode root)
    {
        foreach (var attrList in root.DescendantNodes().OfType<AttributeListSyntax>())
        {
            if (attrList.Target?.Identifier.IsKind(SyntaxKind.AssemblyKeyword) != true)
            {
                continue;
            }

            foreach (var attr in attrList.Attributes)
            {
                var resolved = TryExtract(attr);
                if (resolved != null) return resolved;
            }
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
        var name = attr.Name.ToString();
        var lastSegment = name.Substring(name.LastIndexOf('.') + 1);
        if (lastSegment is not ("SidemarkActivitySource" or "SidemarkActivitySourceAttribute"))
        {
            return null;
        }

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
                if (inv.ArgumentList.Arguments.Count != 1) return null;
                return TerminalIdentifier(inv.ArgumentList.Arguments[0].Expression);

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
