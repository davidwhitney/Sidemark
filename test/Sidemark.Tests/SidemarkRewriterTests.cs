namespace Sidemark.Tests;

public class SidemarkRewriterTests : RewriterTestBase
{
    [Fact]
    public void NoDirectives_ReturnsSourceUnchanged()
    {
        const string src = """
            using System.Diagnostics;

            public class S
            {
                public void Do() { var x = 1; }
            }
            """;

        AssertCSharpEquivalent(src, Rewrite(src));
    }

    [Fact]
    public void MethodWithUnnamedActivityComment_StartsScopeWithMethodName()
    {
        const string input = """
            public class S
            {
                public void Do() //?
                {
                    var x = 1;
                }
            }
            """;

        const string expected = """
            public class S
            {
                public void Do()
                {
                    using var __sidemarkScope = ActivitySource.StartActivity("Do");
                    var x = 1;
                }
            }
            """;

        AssertCSharpEquivalent(expected, Rewrite(input));
    }

    [Fact]
    public void MethodWithNamedActivityComment_UsesProvidedName()
    {
        const string input = """
            public class S
            {
                public void Do() //? Custom Name
                {
                }
            }
            """;

        const string expected = """
            public class S
            {
                public void Do()
                {
                    using var __sidemarkScope = ActivitySource.StartActivity("Custom Name");
                }
            }
            """;

        AssertCSharpEquivalent(expected, Rewrite(input));
    }

    [Fact]
    public void LocalDeclarationWithTagComment_EmitsSetTagOnAmbientActivity()
    {
        const string input = """
            public class S
            {
                public void Do() //?
                {
                    var operationId = 123; //?
                }
            }
            """;

        const string expected = """
            public class S
            {
                public void Do()
                {
                    using var __sidemarkScope = ActivitySource.StartActivity("Do");
                    var operationId = 123;
                    System.Diagnostics.Activity.Current?.SetTag("operationId", operationId);
                }
            }
            """;

        AssertCSharpEquivalent(expected, Rewrite(input));
    }

    [Fact]
    public void LocalDeclarationWithNamedTagComment_UsesExplicitKey()
    {
        const string input = """
            public class S
            {
                public void Do() //?
                {
                    var x = 456; //? friendly.Name
                }
            }
            """;

        const string expected = """
            public class S
            {
                public void Do()
                {
                    using var __sidemarkScope = ActivitySource.StartActivity("Do");
                    var x = 456;
                    System.Diagnostics.Activity.Current?.SetTag("friendly.Name", x);
                }
            }
            """;

        AssertCSharpEquivalent(expected, Rewrite(input));
    }

    [Fact]
    public void StatementWithTrailingEventComment_EmitsAddEventBeforeStatement()
    {
        const string input = """
            public class S
            {
                public void Do() //?
                {
                    DoStuff(); //! Working
                }
            }
            """;

        const string expected = """
            public class S
            {
                public void Do()
                {
                    using var __sidemarkScope = ActivitySource.StartActivity("Do");
                    System.Diagnostics.Activity.Current?.AddEvent(new System.Diagnostics.ActivityEvent("Working"));
                    DoStuff();
                }
            }
            """;

        AssertCSharpEquivalent(expected, Rewrite(input));
    }

    [Fact]
    public void StatementWithLeadingEventComment_EmitsAddEventBeforeStatement()
    {
        const string input = """
            public class S
            {
                public void Do() //?
                {
                    //! Working
                    DoStuff();
                }
            }
            """;

        const string expected = """
            public class S
            {
                public void Do()
                {
                    using var __sidemarkScope = ActivitySource.StartActivity("Do");
                    System.Diagnostics.Activity.Current?.AddEvent(new System.Diagnostics.ActivityEvent("Working"));
                    DoStuff();
                }
            }
            """;

        AssertCSharpEquivalent(expected, Rewrite(input));
    }

    [Fact]
    public void TagDirectiveWithoutSignatureMarker_TagsAmbientWithoutCreatingActivity()
    {
        const string input = """
            public class S
            {
                public void Do()
                {
                    var x = 1; //?
                }
            }
            """;

        const string expected = """
            public class S
            {
                public void Do()
                {
                    var x = 1;
                    System.Diagnostics.Activity.Current?.SetTag("x", x);
                }
            }
            """;

        AssertCSharpEquivalent(expected, Rewrite(input));
    }

    [Fact]
    public void EventDirectiveWithoutSignatureMarker_EmitsEventOnAmbientWithoutCreatingActivity()
    {
        const string input = """
            public class S
            {
                public void Do()
                {
                    DoStuff(); //! Working
                }
                void DoStuff() {}
            }
            """;

        var output = Rewrite(input);
        Assert.DoesNotContain("StartActivity", output);
        Assert.Contains("System.Diagnostics.Activity.Current?.AddEvent(new System.Diagnostics.ActivityEvent(\"Working\"))", output);
    }

    [Fact]
    public void MethodWithNoDirectivesAtAll_IsNotInstrumented()
    {
        const string input = """
            public class S
            {
                public void Plain()
                {
                    var x = 1;
                    DoStuff();
                }
                void DoStuff() {}
            }
            """;

        AssertCSharpEquivalent(input, Rewrite(input));
    }

    [Fact]
    public void AssemblyDisableAttribute_ShortCircuitsRewriting()
    {
        const string input = """
            using Sidemark;

            [assembly: DisableSidemark]

            public class S
            {
                public void Do() //?
                {
                    var x = 1; //?
                }
            }
            """;

        AssertCSharpEquivalent(input, Rewrite(input));
    }

    [Fact]
    public void HasDisableAttribute_OverMultipleRoots_ReturnsTrueIfAnyRootHasIt()
    {
        // [assembly: DisableSidemark] is project-wide. A consumer that has all the source roots
        // should propagate the disable signal even when the attribute is in a different file
        // from the one being rewritten.
        const string disablingFile = "[assembly: Sidemark.DisableSidemark]";
        const string instrumentedFile = """
            public class S
            {
                public void Do() //?
                {
                    var x = 1; //?
                }
            }
            """;

        var roots = new[] { disablingFile, instrumentedFile }
            .Select(s => Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(s).GetRoot())
            .ToArray();

        Assert.True(SidemarkRewriter.HasDisableAttribute(roots));
        Assert.True(SidemarkRewriter.HasDisableAttribute(new[] { roots[0] }));
        Assert.False(SidemarkRewriter.HasDisableAttribute(new[] { roots[1] }));
    }

    [Fact]
    public void OptionsDisabled_ShortCircuitsRewriting()
    {
        const string input = """
            public class S
            {
                public void Do() //?
                {
                    var x = 1; //?
                }
            }
            """;

        var options = new SidemarkOptions { Disabled = true };
        AssertCSharpEquivalent(input, Rewrite(input, options));
    }

    [Fact]
    public void CustomActivitySourceExpression_IsHonoured()
    {
        const string input = """
            public class S
            {
                public void Do() //?
                {
                }
            }
            """;

        const string expected = """
            public class S
            {
                public void Do()
                {
                    using var __sidemarkScope = OTelConfig.MyActivitySource.StartActivity("Do");
                }
            }
            """;

        var options = new SidemarkOptions { ActivitySourceExpression = "OTelConfig.MyActivitySource" };
        AssertCSharpEquivalent(expected, Rewrite(input, options));
    }

    [Fact]
    public void CombinedDirectives_RoundtripsTheReadmeExample()
    {
        const string input = """
            using System.Diagnostics;

            public class MyService
            {
                public async Task DoWork() //? Optional Custom Activity Name
                {
                    var operationId = 123; //?
                    var somethingWithANameOveride = 456; //? friendly.Name

                    try
                    {
                        await Task.Delay(100); //! Working
                    }
                    catch (System.Exception ex)
                    {
                        Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
                        throw;
                    }
                }
            }
            """;

        const string expected = """
            using System.Diagnostics;

            public class MyService
            {
                public async Task DoWork()
                {
                    using var __sidemarkScope = OTelConfig.MyActivitySource.StartActivity("Optional Custom Activity Name");
                    var operationId = 123;
                    System.Diagnostics.Activity.Current?.SetTag("operationId", operationId);
                    var somethingWithANameOveride = 456;
                    System.Diagnostics.Activity.Current?.SetTag("friendly.Name", somethingWithANameOveride);

                    try
                    {
                        System.Diagnostics.Activity.Current?.AddEvent(new System.Diagnostics.ActivityEvent("Working"));
                        await Task.Delay(100);
                    }
                    catch (System.Exception ex)
                    {
                        Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
                        throw;
                    }
                }
            }
            """;

        var options = new SidemarkOptions { ActivitySourceExpression = "OTelConfig.MyActivitySource" };
        AssertCSharpEquivalent(expected, Rewrite(input, options));
    }

    [Fact]
    public void CommentsThatAreNotDirectives_ArePreserved()
    {
        const string input = """
            public class S
            {
                public void Do() //?
                {
                    // ordinary comment
                    var x = 1;
                }
            }
            """;

        const string expected = """
            public class S
            {
                public void Do()
                {
                    using var __sidemarkScope = ActivitySource.StartActivity("Do");
                    // ordinary comment
                    var x = 1;
                }
            }
            """;

        AssertCSharpEquivalent(expected, Rewrite(input));
    }

    [Fact]
    public void EventDirectiveOnMethodSignature_EmitsAddEventAtBodyEntry()
    {
        const string input = """
            public class S
            {
                public void Do() //! Greeting
                {
                }
            }
            """;

        const string expected = """
            public class S
            {
                public void Do()
                {
                    System.Diagnostics.Activity.Current?.AddEvent(new System.Diagnostics.ActivityEvent("Greeting"));
                }
            }
            """;

        AssertCSharpEquivalent(expected, Rewrite(input));
    }

    [Fact]
    public void UnnamedEventDirectiveOnMethodSignature_UsesMethodNameAsEvent()
    {
        const string input = """
            public class S
            {
                public void Greet() //!
                {
                }
            }
            """;

        const string expected = """
            public class S
            {
                public void Greet()
                {
                    System.Diagnostics.Activity.Current?.AddEvent(new System.Diagnostics.ActivityEvent("Greet"));
                }
            }
            """;

        AssertCSharpEquivalent(expected, Rewrite(input));
    }

    [Fact]
    public void EventOnSignature_DoesNotCreateNewActivity()
    {
        const string input = """
            public class S
            {
                public void Do() //!
                {
                    var x = 1;
                }
            }
            """;

        var output = Rewrite(input);
        Assert.DoesNotContain("StartActivity", output);
        Assert.Contains("System.Diagnostics.Activity.Current?.AddEvent(new System.Diagnostics.ActivityEvent(\"Do\"))", output);
    }

    [Fact]
    public void CompoundMarkerOnSignature_CreatesActivityAndEmitsEntryEvent()
    {
        const string input = """
            public class S
            {
                public void Handle() //?! Started
                {
                }
            }
            """;

        const string expected = """
            public class S
            {
                public void Handle()
                {
                    using var __sidemarkScope = ActivitySource.StartActivity("Handle");
                    System.Diagnostics.Activity.Current?.AddEvent(new System.Diagnostics.ActivityEvent("Started"));
                }
            }
            """;

        AssertCSharpEquivalent(expected, Rewrite(input));
    }

    [Fact]
    public void CompoundMarkerWithoutPayload_UsesMethodNameForBoth()
    {
        const string input = """
            public class S
            {
                public void Handle() //?!
                {
                }
            }
            """;

        const string expected = """
            public class S
            {
                public void Handle()
                {
                    using var __sidemarkScope = ActivitySource.StartActivity("Handle");
                    System.Diagnostics.Activity.Current?.AddEvent(new System.Diagnostics.ActivityEvent("Handle"));
                }
            }
            """;

        AssertCSharpEquivalent(expected, Rewrite(input));
    }

    [Fact]
    public void CompoundMarker_DoesNotAlsoMatchAsActivityOrEvent()
    {
        // //?! must produce one activity and one event, not duplicate calls
        const string input = """
            public class S
            {
                public void Handle() //?! Started
                {
                }
            }
            """;

        var output = Rewrite(input);
        var startCount = System.Text.RegularExpressions.Regex.Matches(output, "StartActivity").Count;
        var addEventCount = System.Text.RegularExpressions.Regex.Matches(output, "AddEvent").Count;
        Assert.Equal(1, startCount);
        Assert.Equal(1, addEventCount);
    }

    [Fact]
    public void SeparatePlainMarkers_StillCompose()
    {
        // //? on signature + //! on next line still works for finer control
        const string input = """
            public class S
            {
                public void Handle() //?
                                     //! BeforeWork
                {
                }
            }
            """;

        const string expected = """
            public class S
            {
                public void Handle()
                {
                    using var __sidemarkScope = ActivitySource.StartActivity("Handle");
                    System.Diagnostics.Activity.Current?.AddEvent(new System.Diagnostics.ActivityEvent("BeforeWork"));
                }
            }
            """;

        AssertCSharpEquivalent(expected, Rewrite(input));
    }

    [Fact]
    public void CatchClauseWithAnnotation_EmitsSetStatusErrorAtCatchEntry()
    {
        const string input = """
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

        const string expected = """
            using System;

            public class S
            {
                public void Do()
                {
                    using var __sidemarkScope = ActivitySource.StartActivity("Do");
                    try { Work(); }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Activity.Current?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
                        throw;
                    }
                }
                void Work() {}
            }
            """;

        AssertCSharpEquivalent(expected, Rewrite(input));
    }

    [Fact]
    public void CatchClauseAnnotationOnLeadingTriviaOfBlock_AlsoEmitsSetStatus()
    {
        const string input = """
            using System;

            public class S
            {
                public void Do() //?
                {
                    try { Work(); }
                    catch (Exception ex)
                    //?
                    {
                        throw;
                    }
                }
                void Work() {}
            }
            """;

        var output = Rewrite(input);
        Assert.Contains("System.Diagnostics.Activity.Current?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message)", output);
    }

    [Fact]
    public void CatchWithoutDeclaredVariable_EmitsErrorStatusWithoutMessage()
    {
        const string input = """
            using System;

            public class S
            {
                public void Do() //?
                {
                    try { Work(); }
                    catch (Exception) //?
                    {
                        throw;
                    }
                }
                void Work() {}
            }
            """;

        var output = Rewrite(input);
        Assert.Contains("System.Diagnostics.Activity.Current?.SetStatus(System.Diagnostics.ActivityStatusCode.Error)", output);
        Assert.DoesNotContain(".Message", output);
    }

    [Fact]
    public void CatchClauseWithoutAnnotation_IsNotTouched()
    {
        const string input = """
            using System;

            public class S
            {
                public void Do() //?
                {
                    try { Work(); }
                    catch (Exception ex)
                    {
                        throw;
                    }
                }
                void Work() {}
            }
            """;

        var output = Rewrite(input);
        Assert.DoesNotContain("SetStatus", output);
    }

    [Fact]
    public void MethodWithOnlyCatchAnnotation_GetsCatchProcessedWithoutCreatingActivity()
    {
        const string input = """
            using System;

            public class S
            {
                public void Do()
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

        var output = Rewrite(input);
        Assert.DoesNotContain("StartActivity", output);
        Assert.Contains("System.Diagnostics.Activity.Current?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message)", output);
    }

    [Fact]
    public void LocalFunctionWithActivityComment_IsInstrumented()
    {
        const string input = """
            public class S
            {
                public void Do()
                {
                    void Inner() //?
                    {
                        var x = 1; //?
                    }
                    Inner();
                }
            }
            """;

        const string expected = """
            public class S
            {
                public void Do()
                {
                    void Inner()
                    {
                        using var __sidemarkScope = ActivitySource.StartActivity("Inner");
                        var x = 1;
                        System.Diagnostics.Activity.Current?.SetTag("x", x);
                    }
                    Inner();
                }
            }
            """;

        AssertCSharpEquivalent(expected, Rewrite(input));
    }
}
