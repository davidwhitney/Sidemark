using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
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
        var rewritten = new List<ITaskItem>();
        var original = new List<ITaskItem>();

        var options = new SidemarkOptions { Disabled = Disabled };
        if (!string.IsNullOrWhiteSpace(ActivitySourceExpression))
        {
            options.ActivitySourceExpression = ActivitySourceExpression!;
        }

        Directory.CreateDirectory(OutputDirectory);

        var sourceContents = new Dictionary<ITaskItem, string>(Sources.Length);
        foreach (var item in Sources)
        {
            var path = item.GetMetadata("FullPath");
            if (string.IsNullOrEmpty(path))
            {
                path = item.ItemSpec;
            }
            
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

        var resolvedConfig = SidemarkRewriter.ResolveAssemblyConfiguration(sourceContents.Values);
        if (resolvedConfig != null)
        {
            if (!string.IsNullOrEmpty(resolvedConfig.SourceExpression))
            {
                options.ActivitySourceExpression = resolvedConfig.SourceExpression!;
            }
            
            options.Patterns = resolvedConfig.Patterns;
            
            Log.LogMessage(MessageImportance.Normal,
                $"Sidemark: using config source '{options.ActivitySourceExpression}', " +
                $"patterns activity='{options.Patterns.ActivityPattern}' tag='{options.Patterns.TagPattern}' event='{options.Patterns.EventPattern}'");
        }
        else
        {
            foreach (var content in sourceContents.Values)
            {
                var fromAssembly = SidemarkRewriter.ResolveAssemblyActivitySource(content);
                if (!string.IsNullOrEmpty(fromAssembly))
                {
                    options.ActivitySourceExpression = fromAssembly!;
                    Log.LogMessage(MessageImportance.Normal, $"Sidemark: using assembly-attribute source '{fromAssembly}'");
                    break;
                }
            }
        }

        foreach (var item in Sources)
        {
            var path = item.GetMetadata("FullPath");
            if (string.IsNullOrEmpty(path))
            {
                path = item.ItemSpec;
            }

            var source = sourceContents[item];

            // Per-file options carry the source path so the rewriter can emit #line directives
            // mapping the obj-folder output back to the original file for IDE debuggers.
            var fileOptions = options.With(sourceFilePath: path);

            string output;
            try
            {
                output = SidemarkRewriter.Rewrite(source, fileOptions);
            }
            catch (Exception e)
            {
                Log.LogError($"Sidemark: failed to rewrite '{path}': {e.Message}");
                return false;
            }

            if (output == source)
            {
                continue;
            }

            // Prefix a #line 1 directive so anything outside modified method bodies (top-level statements,
            // class/member declarations) also maps back to the original file in the PDB.
            var pathForDirective = SidemarkInjection.EscapePathForLineDirective(path);
            output = $"#line 1 \"{pathForDirective}\"\n{output}";

            var outputPath = Path.Combine(OutputDirectory, BuildOutputName(path));
            File.WriteAllText(outputPath, output);

            var newItem = new TaskItem(outputPath);
            foreach (var name in item.MetadataNames.Cast<string>())
            {
                if (IsReservedMetadata(name)) continue;
                newItem.SetMetadata(name, item.GetMetadata(name));
            }
            newItem.SetMetadata("SidemarkOriginalSource", path);

            original.Add(item);
            rewritten.Add(newItem);
        }

        OriginalSources = original.ToArray();
        RewrittenSources = rewritten.ToArray();

        Log.LogMessage(MessageImportance.Normal, $"Sidemark: rewrote {rewritten.Count} of {Sources.Length} compile items");
        return true;
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
