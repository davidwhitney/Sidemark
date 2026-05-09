using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sidemark.Internal;

namespace Sidemark.Tests;

public class ConfigurationAttributeTests : RewriterTestBase
{
    [Fact]
    public void SidemarkAttribute_PointsToConfigType_UsesConfigsActivitySource()
    {
        const string input = """
            using Sidemark;
            using System.Diagnostics;

            [assembly: Sidemark(typeof(MyConfig))]

            public static class MyConfig
            {
                public static readonly ActivitySource ActivitySource = new("X", "1.0.0");
            }

            public class S
            {
                public void Do() //?
                {
                }
            }
            """;

        var output = Rewrite(input);
        Assert.Contains("MyConfig.ActivitySource.StartActivity(\"Do\")", output);
    }

    [Fact]
    public void ConfigType_NotFoundInSource_StillUsesConventionalSource()
    {
        // The attribute references a type whose declaration is in another file.
        // The rewriter still emits "<TypeName>.ActivitySource" for whatever name the user gave.
        const string input = """
            using Sidemark;

            [assembly: Sidemark(typeof(MyConfig))]

            public class S
            {
                public void Do() //?
                {
                }
            }
            """;

        var output = Rewrite(input);
        Assert.Contains("MyConfig.ActivitySource.StartActivity(\"Do\")", output);
    }

    [Fact]
    public void ConfigType_WithCustomActivityPattern_RecognizesCustomMarker()
    {
        const string input = """
            using Sidemark;
            using System.Diagnostics;

            [assembly: Sidemark(typeof(MyConfig))]

            public static class MyConfig
            {
                public static readonly ActivitySource ActivitySource = new("X", "1.0.0");
                public const string ActivityPattern = "//track";
            }

            public class S
            {
                public void Do() //track
                {
                }
            }
            """;

        var output = Rewrite(input);
        Assert.Contains("MyConfig.ActivitySource.StartActivity(\"Do\")", output);
    }

    [Fact]
    public void ConfigType_WithCustomActivityPattern_IgnoresDefaultMarker()
    {
        const string input = """
            using Sidemark;
            using System.Diagnostics;

            [assembly: Sidemark(typeof(MyConfig))]

            public static class MyConfig
            {
                public static readonly ActivitySource ActivitySource = new("X", "1.0.0");
                public const string ActivityPattern = "//track";
            }

            public class S
            {
                public void Do() //?
                {
                }
            }
            """;

        var output = Rewrite(input);
        Assert.DoesNotContain("StartActivity", output);
    }

    [Fact]
    public void ConfigType_WithCustomTagPattern_AppliesToLocalDeclarations()
    {
        const string input = """
            using Sidemark;
            using System.Diagnostics;

            [assembly: Sidemark(typeof(MyConfig))]

            public static class MyConfig
            {
                public static readonly ActivitySource ActivitySource = new("X", "1.0.0");
                public const string TagPattern = "//tag";
            }

            public class S
            {
                public void Do() //?
                {
                    var x = 1; //tag
                }
            }
            """;

        var output = Rewrite(input);
        Assert.Contains("MyConfig.ActivitySource.StartActivity(\"Do\")", output);
        Assert.Contains("System.Diagnostics.Activity.Current?.SetTag(\"x\", x)", output);
    }

    [Fact]
    public void ConfigType_WithCustomEventPattern_AppliesToStatements()
    {
        const string input = """
            using Sidemark;
            using System.Diagnostics;

            [assembly: Sidemark(typeof(MyConfig))]

            public static class MyConfig
            {
                public static readonly ActivitySource ActivitySource = new("X", "1.0.0");
                public const string EventPattern = "//evt";
            }

            public class S
            {
                public void Do() //?
                {
                    DoStuff(); //evt Working
                }
                void DoStuff() {}
            }
            """;

        var output = Rewrite(input);
        Assert.Contains("System.Diagnostics.Activity.Current?.AddEvent(new System.Diagnostics.ActivityEvent(\"Working\"))", output);
    }

    [Fact]
    public void ConfigType_WithCustomActivityEventPattern_RecognizesCompoundMarker()
    {
        const string input = """
            using Sidemark;
            using System.Diagnostics;

            [assembly: Sidemark(typeof(MyConfig))]

            public static class MyConfig
            {
                public static readonly ActivitySource ActivitySource = new("X", "1.0.0");
                public const string ActivityEventPattern = "//both";
            }

            public class S
            {
                public void Handle() //both Started
                {
                }
            }
            """;

        var output = Rewrite(input);
        Assert.Contains("MyConfig.ActivitySource.StartActivity(\"Handle\")", output);
        Assert.Contains("System.Diagnostics.Activity.Current?.AddEvent(new System.Diagnostics.ActivityEvent(\"Started\"))", output);
    }

    [Fact]
    public void ConfigType_WithoutAnyPatternOverrides_UsesDefaults()
    {
        const string input = """
            using Sidemark;
            using System.Diagnostics;

            [assembly: Sidemark(typeof(MyConfig))]

            public static class MyConfig
            {
                public static readonly ActivitySource ActivitySource = new("X", "1.0.0");
            }

            public class S
            {
                public void Do() //?
                {
                    var x = 1; //?
                    DoStuff(); //! Working
                }
                void DoStuff() {}
            }
            """;

        var output = Rewrite(input);
        Assert.Contains("MyConfig.ActivitySource.StartActivity(\"Do\")", output);
        Assert.Contains("SetTag(\"x\", x)", output);
        Assert.Contains("AddEvent(new System.Diagnostics.ActivityEvent(\"Working\"))", output);
    }

    [Fact]
    public void ConfigurationResolver_AcrossRoots_FindsConfigInOtherFile()
    {
        const string fileA = """
            using Sidemark;
            [assembly: Sidemark(typeof(MyConfig))]
            """;

        const string fileB = """
            using System.Diagnostics;

            public static class MyConfig
            {
                public static readonly ActivitySource ActivitySource = new("X", "1.0.0");
                public const string ActivityPattern = "//hi";
            }
            """;

        var roots = new[] { fileA, fileB }
            .Select(s => CSharpSyntaxTree.ParseText(s).GetRoot())
            .ToList<SyntaxNode>();

        var resolved = ConfigurationResolver.TryResolve(roots);
        Assert.NotNull(resolved);
        Assert.Equal("MyConfig.ActivitySource", resolved!.SourceExpression);
        Assert.Equal("//hi", resolved.Patterns.ActivityPattern);
        Assert.Equal("//?", resolved.Patterns.TagPattern);
        Assert.Equal("//!", resolved.Patterns.EventPattern);
    }
}
