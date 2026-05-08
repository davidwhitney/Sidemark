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
        return RewriteWithRoot(tree, root, opts);
    }

    /// Caller-side variant for hosts (e.g. the MSBuild task) that have already resolved the
    /// assembly-level config across the whole project and don't need a per-file merge pass.
    /// Cuts an extra DescendantNodes walk per file.
    internal static SyntaxTree RewriteResolved(SyntaxTree tree, SidemarkOptions options)
    {
        var root = tree.GetRoot();
        if (options.Disabled || HasDisableAttribute(root))
        {
            return tree;
        }
        return RewriteWithRoot(tree, root, options);
    }

    private static SyntaxTree RewriteWithRoot(SyntaxTree tree, SyntaxNode root, SidemarkOptions options)
    {
        var rewriter = new SidemarkSyntaxRewriter(options);
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

        return opts.With(
            activitySourceExpression: resolved.SourceExpression,
            patterns: resolved.Patterns);
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
