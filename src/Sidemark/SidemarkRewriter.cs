using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Sidemark.Internal;

namespace Sidemark;

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

        // Single-tree callers can't see project-wide [assembly: DisableSidemark]; this per-file
        // check is the best we can do here. The MSBuild task aggregates across all sources and
        // sets options.Disabled instead, then calls RewriteResolved.
        if (opts.Disabled || HasDisableAttribute(root))
        {
            return tree;
        }

        opts = MergeWithInFileConfig(opts, root);
        return RewriteWithRoot(tree, root, opts);
    }

    /// Caller-side variant for hosts (e.g. the MSBuild task) that have already resolved the
    /// assembly-level config and disable state across the whole project and don't need a per-file
    /// merge or per-file disable check.
    internal static SyntaxTree RewriteResolved(SyntaxTree tree, SidemarkOptions options)
    {
        if (options.Disabled) return tree;
        return RewriteWithRoot(tree, tree.GetRoot(), options);
    }

    private static SyntaxTree RewriteWithRoot(SyntaxTree tree, SyntaxNode root, SidemarkOptions options)
    {
        var rewriter = new SidemarkSyntaxRewriter(options);
        var newRoot = rewriter.Visit(root);
        return tree.WithRootAndOptions(newRoot!, tree.Options);
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

    /// True when any of the supplied roots contains `[assembly: DisableSidemark]`. The MSBuild
    /// task uses this to propagate the project-wide disable signal into options.Disabled.
    internal static bool HasDisableAttribute(IEnumerable<SyntaxNode> roots)
    {
        foreach (var root in roots)
        {
            if (HasDisableAttribute(root)) return true;
        }
        return false;
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
