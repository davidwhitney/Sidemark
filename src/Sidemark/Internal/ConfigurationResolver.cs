using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sidemark.Internal;

internal sealed class ResolvedConfiguration
{
    public string? SourceExpression { get; set; }
    public DirectivePatterns Patterns { get; set; } = new();
}

internal static class ConfigurationResolver
{
    public static ResolvedConfiguration? TryResolve(IReadOnlyList<SyntaxNode> roots)
    {
        string? configTypeName = null;
        foreach (var root in roots)
        {
            configTypeName = FindConfigTypeName(root);
            if (configTypeName != null)
            {
                break;
            }
        }
        
        if (configTypeName == null)
        {
            return null;
        }

        var result = new ResolvedConfiguration
        {
            SourceExpression = $"{configTypeName}.ActivitySource"
        };

        foreach (var root in roots)
        {
            var typeDecl = FindTypeDeclaration(root, configTypeName);
            if (typeDecl != null)
            {
                ApplyPatternOverrides(typeDecl, result.Patterns);
                break;
            }
        }

        return result;
    }

    private static string? FindConfigTypeName(SyntaxNode root)
    {
        foreach (var attr in root.AssemblyAttributes())
        {
            if (!attr.MatchesType("SidemarkAttribute")) continue;

            var args = attr.ArgumentList?.Arguments;
            if (args is null || args.Value.Count != 1) continue;

            if (args.Value[0].Expression is TypeOfExpressionSyntax typeOf)
            {
                return typeOf.Type.ToString();
            }
        }
        return null;
    }

    private static TypeDeclarationSyntax? FindTypeDeclaration(SyntaxNode root, string typeName)
    {
        var lastSegment = typeName.LastSegment();
        foreach (var t in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (t.Identifier.ValueText == lastSegment)
            {
                return t;
            }
        }
        return null;
    }

    private static void ApplyPatternOverrides(TypeDeclarationSyntax typeDecl, DirectivePatterns patterns)
    {
        foreach (var member in typeDecl.Members)
        {
            switch (member)
            {
                case FieldDeclarationSyntax field:
                    foreach (var v in field.Declaration.Variables)
                    {
                        TryApplyLiteral(patterns, v.Identifier.ValueText, v.Initializer?.Value);
                    }
                    break;
                case PropertyDeclarationSyntax prop:
                    var initializerValue = prop.Initializer?.Value ?? prop.ExpressionBody?.Expression;
                    TryApplyLiteral(patterns, prop.Identifier.ValueText, initializerValue);
                    break;
            }
        }
    }

    private static void TryApplyLiteral(DirectivePatterns patterns, string memberName, ExpressionSyntax? expression)
    {
        if (expression is not LiteralExpressionSyntax lit) return;
        if (!lit.IsKind(SyntaxKind.StringLiteralExpression)) return;

        var value = lit.Token.ValueText;
        switch (memberName)
        {
            case nameof(DirectivePatterns.ActivityPattern): patterns.ActivityPattern = value; break;
            case nameof(DirectivePatterns.TagPattern): patterns.TagPattern = value; break;
            case nameof(DirectivePatterns.EventPattern): patterns.EventPattern = value; break;
            case nameof(DirectivePatterns.ActivityEventPattern): patterns.ActivityEventPattern = value; break;
        }
    }
}
