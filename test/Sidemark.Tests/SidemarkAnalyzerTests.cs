using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Sidemark.Analyzer;

namespace Sidemark.Tests;

public class SidemarkAnalyzerTests
{
    [Fact]
    public async Task TagOnLocalDeclarationInsideInstrumentedMethod_ProducesNoDiagnostic()
    {
        const string src = """
            public class S
            {
                public void Do() //?
                {
                    var x = 1; //?
                }
            }
            """;

        var diagnostics = await GetAnalyzerDiagnostics(src);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task TagOnLocalDeclarationInsideUninstrumentedMethod_ProducesNoDiagnostic()
    {
        const string src = """
            public class S
            {
                public void Do()
                {
                    var x = 1; //?
                }
            }
            """;

        var diagnostics = await GetAnalyzerDiagnostics(src);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task TagOnNonLocalDeclaration_ReportsSDM001()
    {
        const string src = """
            public class S
            {
                public void Do() //?
                {
                    DoStuff(); //?
                }

                void DoStuff() {}
            }
            """;

        var diagnostics = await GetAnalyzerDiagnostics(src);
        Assert.Contains(diagnostics, d => d.Id == "SDM001");
    }

    [Fact]
    public async Task EmptyEventDirectiveOnStatement_ReportsSDM002()
    {
        const string src = """
            public class S
            {
                public void Do() //?
                {
                    DoStuff(); //!
                }

                void DoStuff() {}
            }
            """;

        var diagnostics = await GetAnalyzerDiagnostics(src);
        Assert.Contains(diagnostics, d => d.Id == "SDM002");
    }

    [Fact]
    public async Task NamedEventDirectiveOnStatement_ProducesNoSDM002()
    {
        const string src = """
            public class S
            {
                public void Do() //?
                {
                    DoStuff(); //! Working
                }

                void DoStuff() {}
            }
            """;

        var diagnostics = await GetAnalyzerDiagnostics(src);
        Assert.DoesNotContain(diagnostics, d => d.Id == "SDM002");
    }

    [Fact]
    public async Task DirectiveOnExpressionBodiedMethod_ReportsSDM003()
    {
        const string src = """
            public class S
            {
                public int Value() //?
                    => 42;
            }
            """;

        var diagnostics = await GetAnalyzerDiagnostics(src);
        Assert.Contains(diagnostics, d => d.Id == "SDM003");
    }

    [Fact]
    public async Task DirectiveOnConstructor_ReportsSDM003()
    {
        const string src = """
            public class S
            {
                public S() //?
                {
                }
            }
            """;

        var diagnostics = await GetAnalyzerDiagnostics(src);
        Assert.Contains(diagnostics, d => d.Id == "SDM003");
    }

    [Fact]
    public async Task DirectiveOnPropertyAccessor_ReportsSDM003()
    {
        const string src = """
            public class S
            {
                public int Value
                {
                    get //?
                    {
                        return 42;
                    }
                }
            }
            """;

        var diagnostics = await GetAnalyzerDiagnostics(src);
        Assert.Contains(diagnostics, d => d.Id == "SDM003");
    }

    [Fact]
    public async Task CompoundMarkerOnLocalDeclaration_ReportsSDM004()
    {
        const string src = """
            public class S
            {
                public void Do() //?
                {
                    var x = 1; //?!
                }
            }
            """;

        var diagnostics = await GetAnalyzerDiagnostics(src);
        Assert.Contains(diagnostics, d => d.Id == "SDM004");
    }

    [Fact]
    public async Task CompoundMarkerOnStatement_ReportsSDM004()
    {
        const string src = """
            public class S
            {
                public void Do() //?
                {
                    DoStuff(); //?! Started
                }
                void DoStuff() {}
            }
            """;

        var diagnostics = await GetAnalyzerDiagnostics(src);
        Assert.Contains(diagnostics, d => d.Id == "SDM004");
    }

    [Fact]
    public async Task CompoundMarkerOnSignature_ProducesNoSDM004()
    {
        const string src = """
            public class S
            {
                public void Do() //?! Started
                {
                }
            }
            """;

        var diagnostics = await GetAnalyzerDiagnostics(src);
        Assert.DoesNotContain(diagnostics, d => d.Id == "SDM004");
    }

    [Fact]
    public async Task DuplicateTagKey_ReportsSDM005()
    {
        const string src = """
            public class S
            {
                public void Do() //?
                {
                    var oldId = 1; //? order.id
                    var newId = 2; //? order.id
                }
            }
            """;

        var diagnostics = await GetAnalyzerDiagnostics(src);
        Assert.Contains(diagnostics, d => d.Id == "SDM005");
    }

    [Fact]
    public async Task DuplicateImplicitTagKey_FromMatchingVariableNames_ReportsSDM005()
    {
        const string src = """
            public class S
            {
                public void Do() //?
                {
                    {
                        var x = 1; //?
                    }
                    {
                        var x = 2; //?
                    }
                }
            }
            """;

        var diagnostics = await GetAnalyzerDiagnostics(src);
        Assert.Contains(diagnostics, d => d.Id == "SDM005");
    }

    [Fact]
    public async Task DistinctTagKeys_ProducesNoSDM005()
    {
        const string src = """
            public class S
            {
                public void Do() //?
                {
                    var a = 1; //?
                    var b = 2; //?
                }
            }
            """;

        var diagnostics = await GetAnalyzerDiagnostics(src);
        Assert.DoesNotContain(diagnostics, d => d.Id == "SDM005");
    }

    [Fact]
    public async Task CatchAnnotationWithPayload_ReportsSDM006()
    {
        const string src = """
            using System;

            public class S
            {
                public void Do() //?
                {
                    try { Work(); }
                    catch (Exception ex) //? errored
                    {
                        throw;
                    }
                }
                void Work() {}
            }
            """;

        var diagnostics = await GetAnalyzerDiagnostics(src);
        Assert.Contains(diagnostics, d => d.Id == "SDM006");
    }

    [Fact]
    public async Task CatchAnnotationWithoutPayload_ProducesNoSDM006()
    {
        const string src = """
            using System;

            public class S
            {
                public void Do() //?
                {
                    try { Work(); }
                    catch (Exception ex) //?
                    {
                        throw;
                    }
                }
                void Work() {}
            }
            """;

        var diagnostics = await GetAnalyzerDiagnostics(src);
        Assert.DoesNotContain(diagnostics, d => d.Id == "SDM006");
    }

    [Fact]
    public async Task CustomTagPattern_FiresSdmOnNewMarkerNotOnDefault()
    {
        const string src = """
            using Sidemark;

            [assembly: Sidemark(typeof(Cfg))]

            public static class Cfg
            {
                public const string TagPattern = "//@";
            }

            public class S
            {
                public void Do() //?
                {
                    DoStuff(); //@
                    DoStuff(); //?
                }
                void DoStuff() {}
            }
            """;

        var diagnostics = await GetAnalyzerDiagnostics(src);

        // SDM001 fires on the `//@` line (the new tag pattern is now active).
        Assert.Contains(diagnostics, d => d.Id == "SDM001" && SourceLine(d) == "        DoStuff(); //@");
        // The `//?` line should no longer trigger SDM001 since `//?` is no longer the tag pattern.
        Assert.DoesNotContain(diagnostics, d => d.Id == "SDM001" && SourceLine(d) == "        DoStuff(); //?");

        static string SourceLine(Diagnostic d) =>
            d.Location.SourceTree?.GetText().Lines[d.Location.GetLineSpan().StartLinePosition.Line].ToString() ?? "";
    }

    [Fact]
    public async Task LocalNamedSidemarkScopeInsideInstrumentedMethod_ReportsSDM007()
    {
        const string src = """
            public class S
            {
                public void Do() //? MyOp
                {
                    var __sidemarkScope = 1;
                }
            }
            """;

        var diagnostics = await GetAnalyzerDiagnostics(src);
        Assert.Contains(diagnostics, d => d.Id == "SDM007");
    }

    [Fact]
    public async Task ParameterNamedSidemarkScopeOnInstrumentedMethod_ReportsSDM007()
    {
        const string src = """
            public class S
            {
                public void Do(int __sidemarkScope) //? MyOp
                {
                }
            }
            """;

        var diagnostics = await GetAnalyzerDiagnostics(src);
        Assert.Contains(diagnostics, d => d.Id == "SDM007");
    }

    [Fact]
    public async Task ForeachVariableNamedSidemarkScopeInsideInstrumentedMethod_ReportsSDM007()
    {
        const string src = """
            using System.Collections.Generic;

            public class S
            {
                public void Do(List<int> xs) //? MyOp
                {
                    foreach (var __sidemarkScope in xs) { }
                }
            }
            """;

        var diagnostics = await GetAnalyzerDiagnostics(src);
        Assert.Contains(diagnostics, d => d.Id == "SDM007");
    }

    [Fact]
    public async Task SidemarkScopeInUninstrumentedMethod_ProducesNoSDM007()
    {
        // No //? on the signature → no scope local injected → no collision.
        const string src = """
            public class S
            {
                public void Do()
                {
                    var __sidemarkScope = 1;
                }
            }
            """;

        var diagnostics = await GetAnalyzerDiagnostics(src);
        Assert.DoesNotContain(diagnostics, d => d.Id == "SDM007");
    }

    [Fact]
    public async Task SidemarkScopeInsideNestedLocalFunction_ProducesNoSDM007()
    {
        // The nested local function has its own scope; the outer method's injection doesn't collide
        // with a local declared inside the nested function.
        const string src = """
            public class S
            {
                public void Do() //? MyOp
                {
                    void Inner()
                    {
                        var __sidemarkScope = 1;
                    }
                    Inner();
                }
            }
            """;

        var diagnostics = await GetAnalyzerDiagnostics(src);
        Assert.DoesNotContain(diagnostics, d => d.Id == "SDM007");
    }

    [Fact]
    public async Task DirectiveInsideNestedLocalFunction_DoesNotEmitDuplicateDiagnostics()
    {
        // SDM001 should fire exactly once on the //? inside the local function — once via the
        // local function's own AnalyzeMethodLike pass, NOT also via the outer method's pass.
        const string src = """
            public class S
            {
                public void Outer() //?
                {
                    void Inner()
                    {
                        DoStuff(); //?
                    }
                    Inner();
                }

                void DoStuff() {}
            }
            """;

        var diagnostics = await GetAnalyzerDiagnostics(src);
        var sdm001 = diagnostics.Where(d => d.Id == "SDM001").ToList();
        Assert.Single(sdm001);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnostics(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "TestAsm",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var withAnalyzers = compilation.WithAnalyzers(
            [new SidemarkAnalyzer()]);

        var all = await withAnalyzers.GetAnalyzerDiagnosticsAsync();
        return all;
    }
}
