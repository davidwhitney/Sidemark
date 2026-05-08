using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sidemark.Internal;
using MSBuildTask = Microsoft.Build.Utilities.Task;

namespace Sidemark.Build;

public sealed class RewriteSidemarkTask : MSBuildTask
{
    [Required]
    public ITaskItem[] Sources { get; set; } = [];

    [Required]
    public string OutputDirectory { get; set; } = string.Empty;

    public string? ActivitySourceExpression { get; set; }

    public bool Disabled { get; set; }

    [Output]
    public ITaskItem[] RewrittenSources { get; set; } = [];

    [Output]
    public ITaskItem[] OriginalSources { get; set; } = [];

    public override bool Execute()
    {
        Directory.CreateDirectory(OutputDirectory);

        var bootOptions = new SidemarkOptions { Disabled = Disabled };
        if (!string.IsNullOrWhiteSpace(ActivitySourceExpression))
        {
            bootOptions.ActivitySourceExpression = ActivitySourceExpression!;
        }

        // Read every source. We can't tell yet which ones contribute config or have directives;
        // the cost is dominated by parsing, not reading.
        var sourceContents = new Dictionary<ITaskItem, string>(Sources.Length);
        foreach (var item in Sources)
        {
            var path = GetItemPath(item);
            try
            {
                sourceContents[item] = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                Log.LogError($"Sidemark: failed to read '{path}': {e.Message}");
                return false;
            }
        }

        // Resolve the assembly-level config. Pre-filter via a cheap text scan so we only parse
        // files that could plausibly contain `[assembly: Sidemark(...)]` or `[assembly: DisableSidemark]`.
        // Any file we parse here gets cached for reuse in the rewrite loop below.
        var parsedTrees = new Dictionary<ITaskItem, SyntaxTree>();
        var configRoots = new List<SyntaxNode>();
        foreach (var item in Sources)
        {
            var src = sourceContents[item];
            if (src.IndexOf("[assembly", StringComparison.Ordinal) < 0) continue;
            var tree = CSharpSyntaxTree.ParseText(src);
            parsedTrees[item] = tree;
            configRoots.Add(tree.GetRoot());
        }

        var options = bootOptions;
        var resolved = ConfigurationResolver.TryResolve(configRoots);
        if (resolved != null)
        {
            options = options.With(
                activitySourceExpression: !string.IsNullOrEmpty(resolved.SourceExpression) ? resolved.SourceExpression : null,
                patterns: resolved.Patterns);
            Log.LogMessage(MessageImportance.Normal,
                $"Sidemark: using config source '{options.ActivitySourceExpression}', " +
                $"patterns activity='{options.Patterns.ActivityPattern}' tag='{options.Patterns.TagPattern}' event='{options.Patterns.EventPattern}'");
        }
        else
        {
            // Fall back to per-file [SidemarkActivitySource] attribute lookup.
            foreach (var content in sourceContents.Values)
            {
                var fromAssembly = SidemarkRewriter.ResolveAssemblyActivitySource(content);
                if (!string.IsNullOrEmpty(fromAssembly))
                {
                    options = options.With(activitySourceExpression: fromAssembly);
                    Log.LogMessage(MessageImportance.Normal, $"Sidemark: using assembly-attribute source '{fromAssembly}'");
                    break;
                }
            }
        }

        // Hash-stamp the resolved options so cached outputs from previous builds with different
        // patterns / source expressions get invalidated.
        var optionsHash = ComputeOptionsHash(options);
        var stampPath = Path.Combine(OutputDirectory, ".sidemark.stamp");
        var previousStamp = File.Exists(stampPath) ? File.ReadAllText(stampPath) : null;
        var stampMatches = previousStamp == optionsHash;
        if (!stampMatches)
        {
            File.WriteAllText(stampPath, optionsHash);
        }

        var patterns = options.Patterns;
        var rewritten = new ConcurrentBag<ITaskItem>();
        var original = new ConcurrentBag<ITaskItem>();
        var failureCount = 0;

        Parallel.ForEach(Sources, item =>
        {
            var path = GetItemPath(item);
            var source = sourceContents[item];

            // Pre-filter: a file with no directive markers at all can't produce a rewrite. Skip
            // parsing entirely. This is the dominant win on real projects where Sidemark is used
            // in a small fraction of files.
            if (!HasAnyDirectivePattern(source, patterns))
            {
                return;
            }

            var outputPath = Path.Combine(OutputDirectory, BuildOutputName(path));

            // Incremental cache: if the stamp matches and the cached output is newer than the
            // source, skip the parse + rewrite and re-register the cached output.
            if (stampMatches && File.Exists(outputPath) &&
                File.GetLastWriteTimeUtc(outputPath) >= File.GetLastWriteTimeUtc(path))
            {
                original.Add(item);
                rewritten.Add(BuildRewrittenItem(item, outputPath, path));
                return;
            }

            // Parse — reuse the tree if the config-resolution pre-filter already parsed it.
            if (!parsedTrees.TryGetValue(item, out var tree))
            {
                tree = CSharpSyntaxTree.ParseText(source);
            }

            var fileOptions = options.With(sourceFilePath: path);
            SyntaxTree rewrittenTree;
            try
            {
                // ResolveResolved: skips the per-file MergeWithInFileConfig walk since `options`
                // is already the project-resolved config.
                rewrittenTree = SidemarkRewriter.RewriteResolved(tree, fileOptions);
            }
            catch (Exception e)
            {
                Log.LogError($"Sidemark: failed to rewrite '{path}': {e.Message}");
                Interlocked.Increment(ref failureCount);
                return;
            }

            var output = rewrittenTree.GetRoot().ToFullString();
            if (output == source)
            {
                return;
            }

            // Prefix the top-of-file #line directive so code outside modified method bodies still
            // maps back to the original source path in the PDB.
            var pathForDirective = SidemarkInjection.EscapePathForLineDirective(path);
            output = $"#line 1 \"{pathForDirective}\"\n{output}";

            File.WriteAllText(outputPath, output);

            original.Add(item);
            rewritten.Add(BuildRewrittenItem(item, outputPath, path));
        });

        if (failureCount > 0)
        {
            return false;
        }

        OriginalSources = original.ToArray();
        RewrittenSources = rewritten.ToArray();

        Log.LogMessage(MessageImportance.Normal, $"Sidemark: rewrote {rewritten.Count} of {Sources.Length} compile items");
        return true;
    }

    private static string GetItemPath(ITaskItem item)
    {
        var path = item.GetMetadata("FullPath");
        return string.IsNullOrEmpty(path) ? item.ItemSpec : path;
    }

    private static bool HasAnyDirectivePattern(string source, DirectivePatterns patterns)
    {
        // Pattern strings often overlap (e.g. "//?" is a substring of "//?!"); covering the four
        // distinct strings is fine and short-circuits on the first hit.
        return source.IndexOf(patterns.ActivityPattern, StringComparison.Ordinal) >= 0
            || source.IndexOf(patterns.TagPattern, StringComparison.Ordinal) >= 0
            || source.IndexOf(patterns.EventPattern, StringComparison.Ordinal) >= 0
            || source.IndexOf(patterns.ActivityEventPattern, StringComparison.Ordinal) >= 0;
    }

    private static ITaskItem BuildRewrittenItem(ITaskItem source, string outputPath, string originalPath)
    {
        var newItem = new TaskItem(outputPath);
        foreach (var name in source.MetadataNames.Cast<string>())
        {
            if (IsReservedMetadata(name)) continue;
            newItem.SetMetadata(name, source.GetMetadata(name));
        }
        newItem.SetMetadata("SidemarkOriginalSource", originalPath);
        return newItem;
    }

    private static string ComputeOptionsHash(SidemarkOptions options)
    {
        var payload = string.Join("|",
            options.ActivitySourceExpression,
            options.Disabled ? "1" : "0",
            options.Patterns.ActivityPattern,
            options.Patterns.TagPattern,
            options.Patterns.EventPattern,
            options.Patterns.ActivityEventPattern);
        using var sha = SHA1.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            sb.Append(b.ToString("x2"));
        }
        return sb.ToString();
    }

    private static string BuildOutputName(string sourcePath)
    {
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var ext = Path.GetExtension(sourcePath);
        var hash = ShortHash(sourcePath);
        return $"{name}.{hash}{ext}";
    }

    private static readonly HashSet<string> ReservedMetadata = new(StringComparer.OrdinalIgnoreCase)
    {
        "FullPath", "RootDir", "Filename", "Extension", "RelativeDir", "Directory",
        "RecursiveDir", "Identity", "ModifiedTime", "CreatedTime", "AccessedTime",
        "DefiningProjectFullPath", "DefiningProjectDirectory",
        "DefiningProjectName", "DefiningProjectExtension"
    };

    private static bool IsReservedMetadata(string name) => ReservedMetadata.Contains(name);

    private static string ShortHash(string s)
    {
        using var sha = SHA1.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
        var sb = new StringBuilder(8);
        for (var i = 0; i < 4; i++)
        {
            sb.Append(bytes[i].ToString("x2"));
        }
        return sb.ToString();
    }
}
