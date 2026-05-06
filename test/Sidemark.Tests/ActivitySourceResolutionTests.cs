namespace Sidemark.Tests;

public class ActivitySourceResolutionTests : RewriterTestBase
{
    [Fact]
    public void MethodLevelAttribute_IsHonored()
    {
        const string input = """
            using Sidemark;

            public class S
            {
                [SidemarkActivitySource(typeof(OTelConfig), nameof(OTelConfig.MyActivitySource))]
                public void Do() //?
                {
                }
            }
            """;

        const string expected = """
            using Sidemark;

            public class S
            {
                [SidemarkActivitySource(typeof(OTelConfig), nameof(OTelConfig.MyActivitySource))]
                public void Do()
                {
                    using var __sidemarkScope = OTelConfig.MyActivitySource.StartActivity("Do");
                }
            }
            """;

        AssertCSharpEquivalent(expected, Rewrite(input));
    }

    [Fact]
    public void ClassLevelAttribute_IsHonored()
    {
        const string input = """
            using Sidemark;

            [SidemarkActivitySource(typeof(OTelConfig), nameof(OTelConfig.MyActivitySource))]
            public class S
            {
                public void Do() //?
                {
                }
            }
            """;

        var output = Rewrite(input);
        Assert.Contains("OTelConfig.MyActivitySource.StartActivity(\"Do\")", output);
    }

    [Fact]
    public void AssemblyLevelAttribute_IsHonored()
    {
        const string input = """
            using Sidemark;

            [assembly: SidemarkActivitySource(typeof(OTelConfig), nameof(OTelConfig.MyActivitySource))]

            public class S
            {
                public void Do() //?
                {
                }
            }
            """;

        var output = Rewrite(input);
        Assert.Contains("OTelConfig.MyActivitySource.StartActivity(\"Do\")", output);
    }

    [Fact]
    public void MethodLevelAttribute_OverridesClassAndAssembly()
    {
        const string input = """
            using Sidemark;

            [assembly: SidemarkActivitySource(typeof(A), nameof(A.S))]

            [SidemarkActivitySource(typeof(B), nameof(B.S))]
            public class C
            {
                [SidemarkActivitySource(typeof(M), nameof(M.S))]
                public void Do() //?
                {
                }
            }
            """;

        var output = Rewrite(input);
        Assert.Contains("M.S.StartActivity(\"Do\")", output);
        Assert.DoesNotContain("A.S.StartActivity", output);
        Assert.DoesNotContain("B.S.StartActivity", output);
    }

    [Fact]
    public void ClassLevelAttribute_OverridesAssembly()
    {
        const string input = """
            using Sidemark;

            [assembly: SidemarkActivitySource(typeof(A), nameof(A.S))]

            [SidemarkActivitySource(typeof(B), nameof(B.S))]
            public class C
            {
                public void Do() //?
                {
                }
            }
            """;

        var output = Rewrite(input);
        Assert.Contains("B.S.StartActivity(\"Do\")", output);
        Assert.DoesNotContain("A.S.StartActivity", output);
    }

    [Fact]
    public void OptionsActivitySource_StillUsedAsFallbackWhenNoAttribute()
    {
        const string input = """
            public class S
            {
                public void Do() //?
                {
                }
            }
            """;

        var options = new SidemarkOptions { ActivitySourceExpression = "Fallback.Source" };
        var output = Rewrite(input, options);
        Assert.Contains("Fallback.Source.StartActivity(\"Do\")", output);
    }

    [Fact]
    public void AttributeWithSuffixSpelling_IsAlsoRecognized()
    {
        const string input = """
            using Sidemark;

            [SidemarkActivitySourceAttribute(typeof(X), nameof(X.S))]
            public class C
            {
                public void Do() //?
                {
                }
            }
            """;

        var output = Rewrite(input);
        Assert.Contains("X.S.StartActivity(\"Do\")", output);
    }

    [Fact]
    public void StringLiteralMemberName_IsAccepted()
    {
        const string input = """
            using Sidemark;

            [SidemarkActivitySource(typeof(X), "S")]
            public class C
            {
                public void Do() //?
                {
                }
            }
            """;

        var output = Rewrite(input);
        Assert.Contains("X.S.StartActivity(\"Do\")", output);
    }

    [Fact]
    public void FullyQualifiedTypeArg_IsHonored()
    {
        const string input = """
            using Sidemark;

            [SidemarkActivitySource(typeof(My.Ns.X), nameof(My.Ns.X.S))]
            public class C
            {
                public void Do() //?
                {
                }
            }
            """;

        var output = Rewrite(input);
        Assert.Contains("My.Ns.X.S.StartActivity(\"Do\")", output);
    }
}
