using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;

namespace Sidemark.Build;

// The task and Sidemark.dll both need Microsoft.CodeAnalysis(.CSharp) at build time. Rather than
// bundling ~12 MB of Roslyn in the NuGet package, we borrow the copy that ships with the running
// .NET SDK (passed in as $(RoslynTargetsPath)). Sidemark only uses parse/rewrite APIs that have
// been stable since Roslyn 1.0, so whatever version the host SDK provides works.
//
// This is a fallback: when Roslyn sits next to the task DLL (the in-repo project-reference layout
// copies it locally) the load context finds it first and this handler never fires.
internal static class RoslynAssemblyResolver
{
    private static int _installed;
    private static string[] _probeDirs = Array.Empty<string>();

    public static void Ensure(string? roslynPath)
    {
        if (Interlocked.Exchange(ref _installed, 1) == 1) return;

        _probeDirs = BuildProbeDirs(roslynPath);
        AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
    }

    private static string[] BuildProbeDirs(string? roslynPath)
    {
        var dirs = new List<string>();
        void Add(string? dir)
        {
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir) && !dirs.Contains(dir!)) dirs.Add(dir!);
        }

        // $(RoslynTargetsPath) points at the Roslyn folder; the compiler assemblies are under bincore.
        if (!string.IsNullOrEmpty(roslynPath))
        {
            Add(Path.Combine(roslynPath!, "bincore"));
            Add(roslynPath);
        }

        // Fallback: the SDK directory hosting this build also carries a copy of Roslyn.
        var baseDir = AppContext.BaseDirectory;
        Add(Path.Combine(baseDir, "Roslyn", "bincore"));
        Add(baseDir);

        return dirs.ToArray();
    }

    private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
    {
        var name = new AssemblyName(args.Name).Name;
        if (name != "Microsoft.CodeAnalysis" && name != "Microsoft.CodeAnalysis.CSharp")
        {
            return null;
        }

        foreach (var dir in _probeDirs)
        {
            var path = Path.Combine(dir, name + ".dll");
            if (File.Exists(path))
            {
                return Assembly.LoadFrom(path);
            }
        }

        return null;
    }
}
