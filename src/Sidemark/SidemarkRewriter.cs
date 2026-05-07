using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Sidemark.Internal;

namespace Sidemark;

public sealed class ResolvedAssemblyConfiguration
{
    public string? SourceExpression { get; set; }
    public DirectivePatterns Patterns { get; set; } = new();
}

public static class SidemarkRewriter
{
    public static string Rewrite(string source, SidemarkOptions? options = null)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var rewritten = Rewrite(tree, options);
        return rewritten.GetRoot().ToFullString();
    }

    public static SyntaxTree Rewrite(SyntaxTree tree, SidemarkOptions? options = null)
    {
        var opts = options ?? SidemarkOptions.Default;
        var root = tree.GetRoot();

        if (opts.Disabled || HasDisableAttribute(root))
        {
            return tree;
        }

        opts = MergeWithInFileConfig(opts, root);

        var rewriter = new SidemarkSyntaxRewriter(opts);
        var newRoot = rewriter.Visit(root);
        return tree.WithRootAndOptions(newRoot!, tree.Options);
    }

    public static ResolvedAssemblyConfiguration? ResolveAssemblyConfiguration(IEnumerable<string> sources)
    {
        var roots = new List<SyntaxNode>();
        foreach (var s in sources)
        {
            roots.Add(CSharpSyntaxTree.ParseText(s).GetRoot());
        }
        
        var resolved = ConfigurationResolver.TryResolve(roots);
        if (resolved == null)
        {
            return null;
        }
        
        return new ResolvedAssemblyConfiguration
        {
            SourceExpression = resolved.SourceExpression,
            Patterns = resolved.Patterns
        };
    }

    public static string? ResolveAssemblyActivitySource(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        return ActivitySourceResolver.ResolveAssemblyLevel(tree.GetRoot());
    }

    private static SidemarkOptions MergeWithInFileConfig(SidemarkOptions opts, SyntaxNode root)
    {
        var resolved = ConfigurationResolver.TryResolve([root]);
        if (resolved == null)
        {
            return opts;
        }

        return new SidemarkOptions
        {
            ActivitySourceExpression = resolved.SourceExpression ?? opts.ActivitySourceExpression,
            Patterns = resolved.Patterns,
            Disabled = opts.Disabled,
            SourceFilePath = opts.SourceFilePath
        };
    }

    private static bool HasDisableAttribute(SyntaxNode root)
    {
        foreach (var attrList in root.DescendantNodes().OfType<AttributeListSyntax>())
        {
            if (attrList.Target?.Identifier.IsKind(SyntaxKind.AssemblyKeyword) != true)
            {
                continue;
            }

            foreach (var attr in attrList.Attributes)
            {
                if (AttributeNameMatching.Matches(attr.Name.ToString(), nameof(DisableSidemarkAttribute)))
                {
                    return true;
                }
            }
        }
        return false;
    }
}
